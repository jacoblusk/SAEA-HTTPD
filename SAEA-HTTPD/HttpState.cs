namespace SAEAHTTPD {
    public enum HttpState {
        RequestLine,
        Headers,
        Body,
        Trailer,
        Finished
    }
}
