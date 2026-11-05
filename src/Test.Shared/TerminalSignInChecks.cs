#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Tui;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;

    /// <summary>The TUI sign-in page, remembered sessions and the account, administration and share menus against a real service.</summary>
    internal static class TerminalSignInChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-terminal-signin-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                await new ServiceSettings { Port = port }.SaveAsync(new ArgueBotPaths(home), token).ConfigureAwait(false);
                string endpoint = (await LocalServiceConnector.ConnectAsync(home, token: token).ConfigureAwait(false)).TrimEnd('/');
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(Path.Combine(home, "runtime", "service.json"), token).ConfigureAwait(false))!;
                service = Process.GetProcessById(descriptor.ProcessId);

                // The page always shows the default credentials, at 60 and 120 columns in both themes, and signs in by keyboard.
                foreach (int width in new[] { 60, 120 })
                foreach (Theme theme in new[] { TerminalTheme.Dark, Theme.Light })
                {
                    ClientSessionStore.Clear(home);
                    HeadlessBackend backend = new HeadlessBackend(width, 20) { ClaimsTerminal = false };
                    SignInScreen screen = new SignInScreen(home, endpoint, "", "", false, false);
                    using (TuiApplication app = new TuiApplication(backend))
                    {
                        app.Theme = theme; screen.Configure(app, token); app.Theme = theme; app.Start();
                        await PumpAsync(app, () => app.Modals.Top is DialogModal dialog && dialog.Title == "Sign in", token).ConfigureAwait(false);
                        string frame = Snapshot.ToText(app.CaptureFrame()!);
                        foreach (string expected in new[] { IdentitySeed.Email, IdentitySeed.Token, "Server URL" })
                            if (!frame.Contains(expected, StringComparison.Ordinal)) throw new InvalidDataException("The sign-in page at " + width + " columns does not show " + expected + ":\n" + frame);
                        await TerminalSuites.SaveEvidenceAsync("signin-" + width + "-" + theme.Name, frame, token).ConfigureAwait(false);
                        backend.FeedInput(IdentitySeed.Email); app.PumpInputOnce();
                        backend.FeedKey("tab"); app.PumpInputOnce();
                        backend.FeedInput(IdentitySeed.Password); app.PumpInputOnce();
                        app.RenderOnce();
                        
                        backend.FeedKey("ctrl+s");
                        await PumpAsync(app, () => screen.Client != null, token).ConfigureAwait(false);
                        app.Stop();
                    }
                    Require(screen.Session!.Details.User.Email == IdentitySeed.Email && screen.Session.IsSession && screen.Session.IsSystemAdmin, "Keyboard sign-in did not sign in the default administrator.");
                    ClientSession remembered = ClientSessionStore.Load(home) ?? throw new InvalidDataException("A successful sign-in was not remembered.");
                    Require(remembered.Token.StartsWith(SessionProtector.Prefix, StringComparison.Ordinal) && ClientSessionStore.SameServer(remembered.Server, endpoint) && !File.ReadAllText(ClientSessionStore.PathFor(home)).Contains(IdentitySeed.Password + "\"", StringComparison.Ordinal), "The remembered session must hold the server and session token, never the password.");
                    if (!OperatingSystem.IsWindows()) Require((File.GetUnixFileMode(ClientSessionStore.PathFor(home)) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "The remembered session must be private.");
                    screen.Client!.Dispose();
                }

                // Distinct, inline messages for each failure; values are kept because the form stays open.
                SignInScreen checks = new SignInScreen(home, endpoint, "", "", false, false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = "not a url", Email = "a", Password = "b" }, token), "Enter a server URL").ConfigureAwait(false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = "http://127.0.0.1:1", Email = "a", Password = "b" }, token), "Cannot reach").ConfigureAwait(false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = "http://arguebot.example.test:4317", Email = "a", Password = "b" }, token), "plain HTTP").ConfigureAwait(false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = endpoint, Email = IdentitySeed.Email, Password = "wrong-password" }, token), "The email or password is not correct.").ConfigureAwait(false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = endpoint, Email = IdentitySeed.Email }, token), "Enter an email and a password").ConfigureAwait(false);
                await Expect(() => checks.SignInAsync(null, null, new SignInForm { Server = endpoint, Token = "abt_not_a_real_token" }, token), "bearer token is not valid").ConfigureAwait(false);
                await checks.SignInAsync(null, null, new SignInForm { Server = endpoint, Token = IdentitySeed.Token }, token).ConfigureAwait(false);
                Require(checks.Session!.Details.PrincipalType == "Credential" && !checks.Session.IsSession, "Token sign-in did not authenticate as the credential.");

                // A shared email in two tenants asks for a tenant.
                using (ArgueBotClient admin = LocalServiceConnector.CreateClient(endpoint, home, IdentitySeed.Token))
                {
                    Tenant other = await admin.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = "Terminal tenant" }, token).ConfigureAwait(false);
                    await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { Email = "shared@example.test", Password = "shared-password", FirstName = "Shared" }, token).ConfigureAwait(false);
                    admin.TenantId = other.Id;
                    await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { Email = "shared@example.test", Password = "shared-password", FirstName = "Shared" }, token).ConfigureAwait(false);
                }
                await Expect(() => new SignInScreen(home, endpoint, "", "", false, false).SignInAsync(null, null, new SignInForm { Server = endpoint, Email = "shared@example.test", Password = "shared-password" }, token), "several tenants").ConfigureAwait(false);

                // The workspace shows the identity; administrators see Account and Administration; a member sees only Account.
                Conversation conversation = await checks.Client!.CreateConversationAsync("Terminal share fixture", token).ConfigureAwait(false);
                foreach (bool administrator in new[] { true, false })
                {
                    SignInScreen who = new SignInScreen(home, endpoint, "", "", false, false);
                    if (administrator) await who.SignInAsync(null, null, new SignInForm { Server = endpoint, Email = IdentitySeed.Email, Password = IdentitySeed.Password }, token).ConfigureAwait(false);
                    else
                    {
                        string email = "member-terminal@example.test";
                        await checks.Client.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { Email = email, Password = "member-password", FirstName = "Terminal" }, token).ConfigureAwait(false);
                        who = new SignInScreen(home, endpoint, "", "", false, false);
                        await who.SignInAsync(null, null, new SignInForm { Server = endpoint, Email = email, Password = "member-password" }, token).ConfigureAwait(false);
                    }
                    HeadlessBackend backend = new HeadlessBackend(120, 40) { ClaimsTerminal = false };
                    using (ArgueBotClient client = who.Client!)
                    using (TuiApplication app = new TuiApplication(backend))
                    {
                        ConversationApp workspace = new ConversationApp(client, who.Session, false); workspace.Configure(app); app.Start();
                        await PumpAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal share fixture", StringComparison.Ordinal), token, () => workspace.RefreshAsync(token)).ConfigureAwait(false);
                        string screen = Snapshot.ToText(app.CaptureFrame()!);
                        Require(screen.Contains(who.Session!.Details.Tenant.Name, StringComparison.Ordinal), "The workspace does not show the tenant:\n" + screen);
                        backend.FeedKey("f2");
                        await PumpAsync(app, () => app.Modals.Top is ChoiceDialog, token).ConfigureAwait(false);
                        string menu = string.Join("\n", ((ChoiceDialog)app.Modals.Top!).Choices);
                        Require(menu.Contains(ChoiceDialog.Heading + "Account", StringComparison.Ordinal) && menu.Contains("Sign out", StringComparison.Ordinal) && menu.Contains("Switch server", StringComparison.Ordinal), "The Menu has no Account section:\n" + menu);
                        Require(menu.Contains(ChoiceDialog.Heading + "Administration", StringComparison.Ordinal) == administrator && menu.Contains("Tenants", StringComparison.Ordinal) == administrator, administrator ? "Administrators must see Administration and Tenants." : "Members must not see Administration.");
                        await TerminalSuites.SaveEvidenceAsync("signin-menu-" + (administrator ? "admin" : "member"), menu, token).ConfigureAwait(false);
                        if (administrator)
                        {
                            // Share the open conversation from the Menu.
                            ClickChoice(app, backend, "Share a read-only link");
                            await PumpAsync(app, () => app.Modals.Top is ChoiceDialog dialog && (dialog.Title ?? "").StartsWith("Share ", StringComparison.Ordinal), token).ConfigureAwait(false);
                            ClickChoice(app, backend, "Create a read-only link");
                            await PumpAsync(app, () => app.Modals.Top is ChoiceDialog dialog && dialog.Title == "The link stops working after", token).ConfigureAwait(false);
                            ClickChoice(app, backend, "1 day");
                            await PumpAsync(app, () => app.Modals.Top is ChoiceDialog dialog && (dialog.Title ?? "").StartsWith("Include", StringComparison.Ordinal), token).ConfigureAwait(false);
                            ClickChoice(app, backend, "No: only the debate turns");
                            await PumpAsync(app, () => app.Modals.Top is MessageDialog, token).ConfigureAwait(false);
                            string panel = ((MessageDialog)app.Modals.Top!).Content;
                            Require(panel.Contains(endpoint + "/share/", StringComparison.Ordinal), "The share panel does not show the link:\n" + panel);
                            string link = panel.Split('\n').First(line => line.Contains("/share/", StringComparison.Ordinal)).Trim();
                            using (HttpClient anonymous = new HttpClient())
                                Require((await anonymous.GetAsync(link.Replace("/share/", "/v2.0/api/shares/", StringComparison.Ordinal), token).ConfigureAwait(false)).StatusCode == HttpStatusCode.OK, "The TUI share link does not open.");
                            backend.FeedKey("escape");
                        }
                        backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                        app.Stop();
                    }
                }
            }
            finally
            {
                if (service != null) { try { if (!service.HasExited) service.Kill(true); await service.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } finally { service.Dispose(); } }
                await ServiceLifecycleSuites.DeleteStoppedHomeAsync(home).ConfigureAwait(false);
            }
        }

        private static void ClickChoice(TuiApplication app, HeadlessBackend backend, string text)
        {
            app.RenderOnce(); string[] rows = Snapshot.ToText(app.CaptureFrame()!).Split('\n');
            int row = Array.FindIndex(rows, line => line.Contains(text, StringComparison.Ordinal));
            if (row < 0) throw new InvalidDataException("Expected a visible choice: " + text + "\n" + string.Join("\n", rows));
            int column = rows[row].IndexOf(text, StringComparison.Ordinal) + 1;
            backend.FeedMouse(new MouseEvent(MouseEventKind.Press, MouseButton.Left, column, row, KeyModifiers.None, 1));
            backend.FeedMouse(new MouseEvent(MouseEventKind.Release, MouseButton.Left, column, row, KeyModifiers.None, 1));
        }
        private static async Task PumpAsync(TuiApplication app, Func<bool> condition, CancellationToken token, Func<Task>? between = null)
        {
            for (int index = 0; index < 1500; index++)
            {
                token.ThrowIfCancellationRequested(); app.PumpInputOnce(); app.RenderOnce();
                if (condition()) return;
                if (between != null && index % 50 == 0) _ = between();
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("The terminal did not reach the expected screen. Current modal: " + (app.Modals.Top is DialogModal dialog ? dialog.Title : "none") + "\n" + Snapshot.ToText(app.CaptureFrame()!));
        }
        private static async Task Expect(Func<Task> action, string text)
        {
            try { await action().ConfigureAwait(false); }
            catch (InvalidOperationException ex) when (ex.Message.Contains(text, StringComparison.Ordinal)) { return; }
            catch (Exception ex) { throw new InvalidDataException("Expected a message containing '" + text + "' but got " + ex.GetType().Name + ": " + ex.Message); }
            throw new InvalidDataException("Expected a message containing '" + text + "' but sign-in succeeded.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
#endif
