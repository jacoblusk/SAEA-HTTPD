using System;
using System.IO;
using System.Text;
using System.Net.Sockets;
using log4net;

namespace SAEAHTTPD {
	public class HttpClient {
        private const char[] HEADER_SPLIT = { ':' };
        public Socket Socket { get; internal set; }
        public Stream RequestStream { get; private set; }
        public HttpResponse Response { get; private set; }
        public HttpRequest Request { get; private set; }

        internal HttpState State { get; private set; }
        internal int LastActive { get; set; }
        internal byte[] ResponseBytes { get; set; }
        internal int ResponseBytesRemaining { get; set; }
        internal int ResponseBytesOffset { get; set; }

        private static readonly ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private long requestBytesOffset = 0;
        private int transferEncodedChunkSize = 0;
        private int transferEncodedChunkRemaining = 0;

		public HttpClient () {
            this.RequestStream = new MemoryStream();
            this.State = HttpState.RequestLine;
            this.Request = new HttpRequest();
            this.Response = new HttpResponse();
		}

        public void ProcessHTTP() {
            var memoryStream = this.RequestStream as MemoryStream;
            //Uses the underlying buffer instead of creating a new array with ToArray
            byte[] requestBytes = memoryStream.GetBuffer();
            if(this.State == HttpState.RequestLine) {
                for (int i = 0; i < this.RequestStream.Length; i++) {
                    if (requestBytes[i] == '\n') {
                        string requestLine = Encoding.UTF8.GetString(requestBytes, 0, i - 1);
                        log.Debug(string.Format("Request line, {0}", requestLine));
                        string[] request = requestLine.Split(' ');
                        if (request.Length != 3)
                            throw new HttpProcessException("Request line must be seperated by 3 spaces");

                        this.Request.Method = request[0];
                        this.Request.RequestURI = request[1];
                        this.Request.Version = request[2];
                        this.State = HttpState.Headers;
                        this.requestBytesOffset = i;
                        break;
                    }
                }
            } 
            if(State == HttpState.Headers) {
                for(int i = (int)requestBytesOffset; i < this.RequestStream.Length; i++) {
                    if (requestBytes[i] == '\n' && requestBytes[i - 1] == '\r') {
                        if ((i + 1) < this.RequestStream.Length && requestBytes[i + 1] == '\r' && requestBytes[i + 2] == '\n') {
                            log.Debug("End of headers");
                            if (this.Request.Length != 0 || this.Request.TransferEncoding) {
                                this.Request.Body = new byte[Request.Length];
                                this.State = HttpState.Body;
                            }
                            else State = HttpState.Finished;
                            this.requestBytesOffset = i + 3;
                            break;
                        } else {
                            string headerString = Encoding.UTF8.GetString(requestBytes, (int)this.requestBytesOffset + 1, i - (int)this.requestBytesOffset);
                            if (!String.IsNullOrEmpty(headerString) && !String.IsNullOrWhiteSpace(headerString)) {
                                string[] header = headerString.Split(HEADER_SPLIT, 2, StringSplitOptions.RemoveEmptyEntries);
                                if (header.Length != 2)
                                    throw new HttpProcessException(string.Format("Malformed header [{0}]", headerString));

                                string fieldName = header[0].Trim();
                                string value = header[1].Trim();
                                //Field names are case insensitive
                                if (fieldName.ToLower() == "content-length") {
                                    int length = 0;
                                    if (!int.TryParse(value, out length))
                                        throw new HttpProcessException("Content-Length not an integer value");
                                    this.Request.Length = length;
                                }

                                //Even if there was a content-length, transfer-encoding takes precedence
                                if (fieldName.ToLower() == "transfer-encoding" && value == "chunked") {
                                    log.Debug("Transfer-Encoded message");
                                    this.Request.TransferEncoding = true;
                                    this.Request.TransferEncodedBody = new MemoryStream();
                                }

                                this.Request.Headers.Add(fieldName, value);
                                log.Debug(string.Format("Header: {0} => {1}", fieldName, value));
                                this.requestBytesOffset = i;
                            }
                        }
                    }
                }
            }
            if (this.State == HttpState.Body) {
                if (this.Request.TransferEncoding) {
                    for (int i = (int)this.requestBytesOffset; i < this.RequestStream.Length; i++) {
                        if(this.transferEncodedChunkRemaining == 0 && requestBytes[i] == '\n' && requestBytes[i - 1] == '\r') {
                            //Might need to consider endianess here
                            this.transferEncodedChunkSize = BitConverter.ToInt32(requestBytes, (int)requestBytesOffset);
                            if(this.transferEncodedChunkSize == 0) {
                                //Followed by a trailer of entity headers
                                this.Request.Body = this.Request.TransferEncodedBody.ToArray();
                                this.Request.TransferEncodedBody.Dispose();
                                this.State = HttpState.Trailer;

                            }
                            this.transferEncodedChunkRemaining = this.transferEncodedChunkSize;
                            log.Debug(string.Format("Transfer Encoded Chunk Size: {0}", this.transferEncodedChunkSize));
                        } else if(transferEncodedChunkRemaining != 0 && requestBytes[i] == '\n' && requestBytes[i - 1] == '\r') {
                            this.Request.TransferEncodedBody.Write(requestBytes, (int)this.requestBytesOffset, this.transferEncodedChunkSize);
                            this.transferEncodedChunkRemaining = 0;
                        }
                    }
                } else {
                    //A non-transfer encoded request
                    long remaining = this.RequestStream.Length - this.requestBytesOffset;
                    if (remaining >= Request.Length) {
                        Buffer.BlockCopy(requestBytes, (int)this.requestBytesOffset, this.Request.Body, 0, this.Request.Length);
                        this.State = HttpState.Finished;
                    }
                }
            }
            if(this.State == HttpState.Trailer) {
                //Will need to find a way to test transfer-encoding, but for now we'll finisht the request
                this.State = HttpState.Finished;
            }
        }

        public void Reset() {
            this.Socket = null;
            //No preformance benefits to setting the position to 0, as opposed to creating a new memory stream
            this.RequestStream = new MemoryStream();
            this.Request = new HttpRequest();
            //Not useful to call Dispose, but perhaps in the future if a change in streams
            this.Response.OutputStream.Dispose();
            this.Response = new HttpResponse();
            this.State = HttpState.RequestLine;
        }
	}
}

