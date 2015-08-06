using SAEAHTTPD;
using System.IO;
using System.Net;

namespace Example {
	class MainClass {
		public static void Main (string[] args) {
            HttpServer server = new HttpServer(10, 20, 1024);
            server.OnHttpRequest += server_OnHttpRequest;
            server.Start(new IPEndPoint(IPAddress.Any, 9001));
		}

        static void server_OnHttpRequest(object sender, HttpRequestArgs e) {
            e.Response.Status = SAEAHTTPD.HttpStatusCode.OK;
            e.Response.ReasonPhrase = "OK";
            using (TextWriter writer = new StreamWriter(e.Response.OutputStream)) {
                writer.Write("Hello, world!");
            }
        }
	}
}
