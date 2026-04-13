using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Handler;

namespace NYCLauncher.Core
{
    public class ResourceSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new ResourceSchemeHandler();
        }
    }

    public class ResourceSchemeHandler : ResourceHandler
    {
        private static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {".html","text/html"},{".css","text/css"},{".js","application/javascript"},
            {".png","image/png"},{".jpg","image/jpeg"},{".jpeg","image/jpeg"},
            {".gif","image/gif"},{".svg","image/svg+xml"},{".ico","image/x-icon"},
            {".woff","font/woff"},{".woff2","font/woff2"},{".ttf","font/ttf"}
        };

        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            Task.Run(() =>
            {
                try
                {
                    var uri = new Uri(request.Url);
                    string path = uri.AbsolutePath.TrimStart('/');
                    if (string.IsNullOrEmpty(path)) path = "index.html";

                    string resName = "NYCLauncher.Web." + path.Replace('/', '.').Replace('\\', '.');
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);

                    if (stream != null)
                    {
                        string ext = Path.GetExtension(path);
                        string mime;
                        if (!Mime.TryGetValue(ext, out mime)) mime = "application/octet-stream";

                        var ms = new MemoryStream((int)stream.Length);
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        stream.Dispose();

                        ResponseLength = ms.Length;
                        MimeType = mime;
                        StatusCode = 200;
                        Stream = ms;
                        Headers.Add("Access-Control-Allow-Origin", "*");
                        Headers.Add("Cache-Control", "max-age=31536000");
                    }
                    else
                    {
                        StatusCode = 404;
                        MimeType = "text/plain";
                        Stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("404"));
                    }
                }
                catch
                {
                    StatusCode = 500;
                    MimeType = "text/plain";
                    Stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("500"));
                }
                callback.Continue();
            });
            return CefReturnValue.ContinueAsync;
        }
    }
}
