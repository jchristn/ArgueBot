namespace ArgueBot.Tui
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using TUIKit.Hosting;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string? home = null; string? endpoint = null; bool autoStart = true; bool allowHttp = false;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--server" && index + 1 < args.Length) endpoint = args[++index];
                else if (args[index] == "--no-start") autoStart = false;
                else if (args[index] == "--allow-http") allowHttp = true;
                else { Console.WriteLine("ArgueBot.Tui [--home PATH] [--server URL] [--allow-http] [--no-start]. Starts or attaches to the local service, then signs in. --server signs in to another service; --allow-http permits plain HTTP to a remote server. ARGUEBOT_TOKEN signs in with a bearer token."); return args[index] == "--help" ? 0 : 2; }
            }
            if (Console.IsInputRedirected || Console.IsOutputRedirected) { Console.Error.WriteLine("Interactive terminal required. Use ArgueBot.Cli for redirected input/output."); return 2; }
            string resolvedHome = new ArgueBotPaths(home).Home;
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                try
                {
                    ClientSession? remembered = ClientSessionStore.Load(resolvedHome);
                    // An explicit --server wins; otherwise a remembered remote server; otherwise start or attach to the local service.
                    string server = endpoint
                        ?? (remembered != null && Uri.TryCreate(remembered.Server, UriKind.Absolute, out Uri? previous) && !previous.IsLoopback ? remembered.Server : null)
                        ?? await LocalServiceConnector.ConnectAsync(resolvedHome, null, autoStart, lifetime.Token).ConfigureAwait(false);
                    string? environmentToken = Environment.GetEnvironmentVariable("ARGUEBOT_TOKEN");
                    string notice = ""; bool focusServer = false; bool firstScreen = true;
                    while (true)
                    {
                        remembered = ClientSessionStore.Load(resolvedHome);
                        bool allow = allowHttp || (remembered != null && remembered.AllowInsecureHttp && ClientSessionStore.SameServer(remembered.Server, server));
                        ArgueBotClient? client = null; TuiSession? session = null;
                        string? candidate = !string.IsNullOrWhiteSpace(environmentToken) ? environmentToken : remembered != null && ClientSessionStore.SameServer(remembered.Server, server) && (!remembered.ExpiresUtc.HasValue || remembered.ExpiresUtc > DateTime.UtcNow) ? remembered.Token : null;
                        if (candidate != null && !focusServer)
                        {
                            ArgueBotClient attempt = LocalServiceConnector.CreateClient(server, resolvedHome, candidate, allow);
                            try
                            {
                                TokenDetails details = await attempt.TokenDetailsAsync(lifetime.Token).ConfigureAwait(false);
                                bool fromEnvironment = !string.IsNullOrWhiteSpace(environmentToken);
                                session = new TuiSession(resolvedHome, server, allow, details, candidate, fromEnvironment ? details.ExpiresUtc : remembered!.ExpiresUtc ?? details.ExpiresUtc) { Persist = !fromEnvironment };
                                client = attempt;
                            }
                            catch (DomainException ex) when (ex.StatusCode == 401)
                            {
                                attempt.Dispose();
                                notice = !string.IsNullOrWhiteSpace(environmentToken) ? "ARGUEBOT_TOKEN was not accepted (" + ex.Code + "). Sign in instead." : "Your session ended. Sign in again.";
                                environmentToken = null;
                            }
                            catch (Exception) { attempt.Dispose(); throw; }
                        }
                        if (client == null)
                        {
                            SignInScreen screen = new SignInScreen(resolvedHome, server, remembered?.Email ?? "", notice, focusServer, allow);
                            await TuiApp.RunAsync(app => screen.Configure(app, lifetime.Token), lifetime.Token).ConfigureAwait(false);
                            if (screen.Client == null || screen.Session == null) return 0;
                            client = screen.Client; session = screen.Session; server = session.Server; firstScreen = false;
                        }
                        using (client)
                        {
                            ConversationApp workspace = new ConversationApp(client, session, firstScreen);
                            firstScreen = false;
                            await workspace.RunAsync(lifetime.Token).ConfigureAwait(false);
                            focusServer = false; notice = "";
                            switch (workspace.Exit)
                            {
                                case TuiExit.Quit: return 0;
                                case TuiExit.SignOut:
                                    if (session!.IsSession) { try { await client.LogoutAsync(lifetime.Token).ConfigureAwait(false); } catch (DomainException) { } }
                                    if (session.Persist) ClientSessionStore.Clear(resolvedHome);
                                    environmentToken = null; notice = "Signed out.";
                                    break;
                                case TuiExit.SwitchServer: environmentToken = null; focusServer = true; notice = "Enter the server to sign in to."; break;
                                default: environmentToken = null; notice = "Your session ended or was revoked. Sign in again."; break;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
    }
}
