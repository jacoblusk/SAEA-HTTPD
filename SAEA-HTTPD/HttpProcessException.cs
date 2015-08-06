using System;

namespace SAEAHTTPD {
    public class HttpProcessException : Exception {
        public HttpProcessException() { }
        public HttpProcessException(string message) : base(message) { }
        public HttpProcessException(string message, Exception inner) : base(message, inner) { }
    }
}
