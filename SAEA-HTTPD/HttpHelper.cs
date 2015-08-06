using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace SAEAHTTPD {
    internal static class HttpHelper {
        private static readonly byte[] CRLF = new byte[] {(byte)'\r', (byte)'\n' };
        private static readonly string DATE_FORMAT = "ddd, dd MMM yyyy hh:mm:ss";
        public static byte[] BuildResponse(this HttpResponse resp) {
            using (MemoryStream stream = new MemoryStream()) {
                byte[] responseBody = ((MemoryStream)resp.OutputStream).ToArray();
                byte[] statusLine = Encoding.UTF8.GetBytes(string.Format("HTTP/1.1 {0} {1}\r\n", (int)resp.Status, resp.ReasonPhrase));
                stream.Write(statusLine, 0, statusLine.Length);

                if (responseBody.Length > 0) {
                    resp.Headers.Add("Content-Length", responseBody.Length.ToString());
                }

                //Add the date if it wasn't already added
                if(!resp.Headers.ContainsKey("Date"))
                    resp.Headers.Add("Date", DateTime.Now.ToUniversalTime().ToString(DATE_FORMAT) + " GMT");

                var iterator = resp.Headers.GetEnumerator();
                iterator.MoveNext();

                while (true) {
                    var header = iterator.Current;
                    byte[] headerString = Encoding.UTF8.GetBytes(string.Format("{0}: {1}", header.Key, header.Value));
                    stream.Write(headerString, 0, headerString.Length);
                    if (iterator.MoveNext()) {
                        stream.Write(CRLF, 1, 1);
                    } else {
                        stream.Write(CRLF, 0, CRLF.Length);
                        stream.Write(CRLF, 0, CRLF.Length);
                        break;
                    }
                }

                stream.Write(responseBody, 0, responseBody.Length);
                return stream.ToArray();
            }
        }
    }
}
