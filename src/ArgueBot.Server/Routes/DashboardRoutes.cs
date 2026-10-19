namespace ArgueBot.Server.Routes
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    internal sealed class DashboardRoutes
    {
        private readonly string? _Directory;
        internal DashboardRoutes()
        {
            string? configured = Environment.GetEnvironmentVariable("ARGUEBOT_DASHBOARD_DIR");
            if (!string.IsNullOrWhiteSpace(configured)) { _Directory = Path.GetFullPath(configured); return; }
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "dashboard", "index.html"))) { _Directory = Path.Combine(AppContext.BaseDirectory, "dashboard"); return; }
            DirectoryInfo? parent = new DirectoryInfo(AppContext.BaseDirectory);
            while (parent != null)
            {
                string candidate = Path.Combine(parent.FullName, "dashboard", "dist");
                if (File.Exists(Path.Combine(candidate, "index.html"))) { _Directory = candidate; return; }
                parent = parent.Parent;
            }
        }
        internal void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Dynamic.Add(HttpMethod.GET, new Regex(@"^/(?:$|dashboard(?:/.*)?$|assets/[A-Za-z0-9._-]+$)", RegexOptions.CultureInvariant), ServeAsync);
        }
        private async Task ServeAsync(HttpContextBase http)
        {
            string path = http.Request.Url.RawWithoutQuery;
            string relative = path.StartsWith("/assets/", StringComparison.Ordinal) ? path.TrimStart('/') : "index.html";
            if (_Directory == null) { http.Response.StatusCode = 503; await http.Response.Send("Dashboard is not built. Run npm --prefix dashboard ci and npm --prefix dashboard run build.", http.Token).ConfigureAwait(false); return; }
            string file = Path.GetFullPath(Path.Combine(_Directory, relative));
            if (!file.StartsWith(_Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file)) { http.Response.StatusCode = 404; await http.Response.Send("Asset not found.", http.Token).ConfigureAwait(false); return; }
            http.Response.ContentType = Path.GetExtension(file) switch { ".js" => "text/javascript", ".css" => "text/css", ".svg" => "image/svg+xml", ".png" => "image/png", ".woff2" => "font/woff2", _ => "text/html; charset=utf-8" };
            using (FileStream stream = File.OpenRead(file)) { await http.Response.Send(stream.Length, stream, http.Token).ConfigureAwait(false); }
        }
    }
}
