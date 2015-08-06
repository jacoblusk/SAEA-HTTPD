using System.Collections.Generic;
using System.IO;

namespace SAEAHTTPD {
    public class HttpRequest {
        public string Method { get; internal set; }
        public string RequestURI { get; internal set; }
        public string Version { get; internal set; }
        public int Length { get; internal set; }
        public Dictionary<string, string> Headers { get; private set; }
        public byte[] Body { get; internal set; }

        internal bool TransferEncoding { get; set; }
        internal MemoryStream TransferEncodedBody;

        public HttpRequest() {
            this.Headers = new Dictionary<string, string>();
        }
    }
}
