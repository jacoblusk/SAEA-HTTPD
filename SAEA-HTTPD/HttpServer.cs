using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ServiceModel.Channels;
using System.Timers;
using log4net;

namespace SAEAHTTPD {
    public class HttpServer {
        private readonly List<SocketAsyncEventArgs> connections = new List<SocketAsyncEventArgs>();
        private readonly ConcurrentStack<SocketAsyncEventArgs> readWritePool = new ConcurrentStack<SocketAsyncEventArgs> ();
        private readonly ConcurrentStack<SocketAsyncEventArgs> acceptPool = new ConcurrentStack<SocketAsyncEventArgs> ();
        private bool running = true;
        private int maxAccept, maxConnections, bufferSize;
        private const int Timeout = 10 * 1000;
        private readonly Semaphore enforceMaxClients;
        private readonly Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly BufferManager bufferManager;
        private System.Timers.Timer timeoutTimer;

        private static readonly ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public delegate void HttpRequestHandler(object sender, HttpRequestArgs e);
        public event HttpRequestHandler OnHttpRequest;

        public HttpServer (int maxAccept, int maxConnections, int bufferSize) {
            timeoutTimer = new System.Timers.Timer(Timeout);
            timeoutTimer.Elapsed += timeoutTimer_Elapsed;
            this.maxAccept = maxAccept;
            this.maxConnections = maxConnections;
            this.bufferSize = bufferSize;
            this.enforceMaxClients = new Semaphore (maxConnections, maxConnections);
            this.bufferManager = BufferManager.CreateBufferManager (maxConnections, maxConnections * bufferSize * 2);

            for (int i = 0; i < maxAccept; i++) {
                var acceptArgs = new SocketAsyncEventArgs ();
                acceptArgs.Completed += HandleAcceptCompleted;
                this.acceptPool.Push (acceptArgs);
            }

            for (int i = 0; i < maxConnections; i++) {
                var readWriteArgs = new SocketAsyncEventArgs ();
                var client = new HttpClient ();
                readWriteArgs.UserToken = client;
                readWriteArgs.SetBuffer (this.bufferManager.TakeBuffer (bufferSize), 0, bufferSize);
                readWriteArgs.Completed += HandleReadWriteCompleted;
                this.readWritePool.Push (readWriteArgs);
            }

            timeoutTimer.Start();
        }

        void timeoutTimer_Elapsed(object sender, ElapsedEventArgs e) {
            lock (this.connections) {
                List<SocketAsyncEventArgs> removed = new List<SocketAsyncEventArgs>();
                foreach (SocketAsyncEventArgs conn in connections) {
                    var client = conn.UserToken as HttpClient;
                    int remaining = Environment.TickCount - client.LastActive;
                    log.Debug(string.Format("Remaining time for {0} is {1}", client.Socket.RemoteEndPoint, remaining));
                    if (remaining > Timeout) {
                        removed.Add(conn);
                    }
                }

                foreach (SocketAsyncEventArgs conn in removed) {
                    CloseSocket(conn);
                }
            }
        }

        void client_OnFinishedRequest(object sender, HttpRequestArgs args) {
            if (OnHttpRequest != null) {
                OnHttpRequest(this, args);
            }
        }

        public void Start(IPEndPoint local) {
            this.listener.Bind (local);
            this.listener.Listen (this.maxAccept);

            SocketAsyncEventArgs acceptArgs;
            while (this.running) {
                if (acceptPool.TryPop(out acceptArgs)) {
                    if (!this.listener.AcceptAsync(acceptArgs)) {
                        HandleAccept(acceptArgs);
                    }
                }
                this.enforceMaxClients.WaitOne();
            }
        }

        public void Stop(bool force) {
            this.running = false;
        }

        private void HandleAccept(SocketAsyncEventArgs acceptArgs) {
            log.Debug(string.Format("Accept, {0}", acceptArgs.AcceptSocket.RemoteEndPoint));
            if (acceptArgs.SocketError != SocketError.Success) {
                acceptArgs.AcceptSocket.Close ();
            } else {
                SocketAsyncEventArgs readWriteArgs;
                if (this.readWritePool.TryPop (out readWriteArgs)) {
                    var client = (HttpClient)readWriteArgs.UserToken;
                    client.Socket = acceptArgs.AcceptSocket;
                    client.LastActive = Environment.TickCount;
                    readWriteArgs.AcceptSocket = acceptArgs.AcceptSocket;
                    acceptArgs.AcceptSocket = null;

                    lock (this.connections) {
                        this.connections.Add(readWriteArgs);
                    }

                    if (!client.Socket.ReceiveAsync (readWriteArgs)) {
                        HandleReadWrite (readWriteArgs);
                    }
                }
            }
            this.acceptPool.Push (acceptArgs);
        }

        private void HandleReadWrite(SocketAsyncEventArgs readWriteArgs) {
            var client = (HttpClient)readWriteArgs.UserToken;
            if (readWriteArgs.SocketError != SocketError.Success) {
                log.Warn(string.Format("Socket error {0}", readWriteArgs.SocketError));
                //Socket was already removed
                if (readWriteArgs.SocketError != SocketError.OperationAborted) {
                    CloseSocket(readWriteArgs);
                }
                return;
            }

            switch (readWriteArgs.LastOperation) {
                case SocketAsyncOperation.Receive:
                    HandleRecieve(readWriteArgs);
                    break;
                case SocketAsyncOperation.Send:
                    HandleSend(readWriteArgs);
                    break;
                default:
                    throw new ArgumentException("Unknown last operation!");
            }
        }

        private void StartSend(SocketAsyncEventArgs sendArgs) {
            var client = sendArgs.UserToken as HttpClient;
            client.ResponseBytesRemaining = client.ResponseBytes.Length;
            bufferManager.ReturnBuffer(sendArgs.Buffer);
            sendArgs.SetBuffer(client.ResponseBytes, 0, client.ResponseBytes.Length);
            if (!client.Socket.SendAsync(sendArgs)) {
                HandleReadWrite(sendArgs);
            }
        }

        private void HandleSend(SocketAsyncEventArgs sendArgs) {
            var client = sendArgs.UserToken as HttpClient;
            client.ResponseBytesRemaining -= sendArgs.BytesTransferred;
            if (client.ResponseBytesRemaining > 0) {
                client.ResponseBytesOffset += sendArgs.BytesTransferred;
                if (!client.Socket.SendAsync(sendArgs)) {
                    HandleReadWrite(sendArgs);
                }
            } else {
                log.Debug("Finished sending response");
                sendArgs.SetBuffer(bufferManager.TakeBuffer(this.bufferSize), 0, this.bufferSize);
                CloseSocket(sendArgs);
            }
        }

        private void HandleRecieve(SocketAsyncEventArgs readArgs) {
            var client = readArgs.UserToken as HttpClient;
            client.LastActive = Environment.TickCount;
            if (readArgs.BytesTransferred == 0) {
                CloseSocket(readArgs);
                return;
            }

            client.RequestStream.Write(readArgs.Buffer, readArgs.Offset, readArgs.BytesTransferred);
            try {
                client.ProcessHTTP();
            } catch(HttpProcessException ex) {
                //The request is said to be faulty here
                CloseSocket(readArgs);
                log.Warn(ex);
                return;
            }

            if (client.State == HttpState.Finished) {
                if (OnHttpRequest != null) {
                    OnHttpRequest(this, new HttpRequestArgs(client.Request, client.Response));
                    client.ResponseBytes = client.Response.BuildResponse();
                    log.Debug(Encoding.UTF8.GetString(client.ResponseBytes));
                    StartSend(readArgs);
                    return;
                }
            }

            if (!client.Socket.ReceiveAsync(readArgs)) {
                HandleReadWrite(readArgs);
            }
        }

        private void CloseSocket(SocketAsyncEventArgs args) {
            lock (this.connections) {
                if(!this.connections.Contains(args)) {
                    log.Debug("We already closed this socket");
                    return;
                }
                this.connections.Remove(args);
            }

            log.Debug(string.Format("Closing socket {0}", args.AcceptSocket.RemoteEndPoint));

            try {
                args.AcceptSocket.Shutdown(SocketShutdown.Both);
            } catch (SocketException) { 
                log.Debug("Socket must already be closed");
            }

            args.AcceptSocket.Close();

            ((HttpClient)args.UserToken).Reset();
            this.readWritePool.Push(args);
            this.enforceMaxClients.Release();
        }

        private void HandleReadWriteCompleted (object sender, SocketAsyncEventArgs e) {
            HandleReadWrite (e);
        }

        private void HandleAcceptCompleted (object sender, SocketAsyncEventArgs e) {
            HandleAccept (e);
        }
    }
}

