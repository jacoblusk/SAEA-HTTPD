using System.Collections.Generic;
using System.IO;

namespace SAEAHTTPD {
    public class HttpResponse {
        public Stream OutputStream;
        public string ReasonPhrase;
        public HttpStatusCode Status;
        public Dictionary<string, string> Headers;
        public HttpResponse() {
            this.OutputStream = new MemoryStream();
            this.Headers = new Dictionary<string, string>();
        }
    }
}
