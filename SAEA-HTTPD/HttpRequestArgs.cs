namespace SAEAHTTPD {
    public class HttpRequestArgs {
        public HttpRequest Request { get; private set; }
        public HttpResponse Response { get; private set; }
        public HttpRequestArgs(HttpRequest req, HttpResponse response) {
            this.Request = req;
            this.Response = response;
        }
    }
}
