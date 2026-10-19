namespace ArgueBot.Server.Routes
{
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    internal static class ContractRoutes
    {
        internal static void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/openapi.json", async http =>
            {
                using (Stream document = Assembly.GetExecutingAssembly().GetManifestResourceStream("ArgueBot.openapi.json") ?? throw new FileNotFoundException("Packaged API contract is missing."))
                using (StreamReader reader = new StreamReader(document))
                { http.Response.ContentType = "application/json"; await http.Response.Send(await reader.ReadToEndAsync(http.Token).ConfigureAwait(false), http.Token).ConfigureAwait(false); }
            });
        }
    }
}
