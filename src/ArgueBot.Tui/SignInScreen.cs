namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>
    /// Full-screen sign-in page: server URL, email and password or a bearer token, and a tenant choice when needed.
    /// The default credentials are always shown. A successful sign-in is remembered for the home (never the password).
    /// </summary>
    internal sealed class SignInScreen
    {
        /// <summary>Text shown on the page and beside each field so the defaults are visible at any terminal size.</summary>
        internal const string DefaultsText = "Default administrator: " + IdentitySeed.Email + " / " + IdentitySeed.Password + "   Default token: " + IdentitySeed.Token;
        private readonly string _Home;
        private readonly string _Server;
        private readonly string _Email;
        private readonly string _Notice;
        private readonly bool _FocusServer;
        private readonly bool _AllowInsecureHttp;
        private readonly Func<string, bool, ArgueBotClient> _Factory;
        private readonly Label _Heading = new Label(Text.From("Sign in to ArgueBot"));
        private readonly Label _Defaults = new Label(Text.From(DefaultsText));
        private readonly Label _Message = new Label(Text.From(""));
        private readonly StatusBar _Status = new StatusBar { RightText = "ArgueBot" };

        internal SignInScreen(string home, string server, string email, string notice, bool focusServer, bool allowInsecureHttp, Func<string, bool, ArgueBotClient>? factory = null)
        {
            _Home = home; _Server = server; _Email = email; _Notice = notice; _FocusServer = focusServer; _AllowInsecureHttp = allowInsecureHttp;
            _Factory = factory ?? ((url, allow) => LocalServiceConnector.CreateClient(url, home, "", allow));
        }

        /// <summary>The signed-in client, or null when the user quit.</summary>
        internal ArgueBotClient? Client { get; private set; }
        /// <summary>The signed-in identity, or null when the user quit.</summary>
        internal TuiSession? Session { get; private set; }

        internal void Configure(TuiApplication app, CancellationToken token)
        {
            app.ApplyThemeToWidgets = true; app.HighlightFocusedRegion = false;
            if (app.Theme.Name != "Light") app.Theme = TerminalTheme.Dark;
            app.AddWidget("signin-heading", _Heading, region => region.TopAnchored(1, 1).FillWidth().WithPadding(2, 0, 2, 0));
            app.AddWidget("signin-defaults", _Defaults, region => region.TopAnchored(2, 1).FillWidth().WithPadding(2, 0, 2, 0));
            app.AddWidget("signin-message", _Message, region => region.BottomAnchored(1, 1).FillWidth().WithPadding(2, 0, 2, 0));
            app.AddWidget("signin-status", _Status, region => region.BottomAnchored(0, 1).FillWidth().WithPadding(1, 0, 1, 0));
            _Heading.Content = Text.From("Sign in to ArgueBot", app.Theme.Text).Bold();
            _Defaults.Content = Text.From(DefaultsText, app.Theme.Muted);
            _Message.Content = Text.From(_Notice, app.Theme.Warning);
            _Status.HintSource = () => new[] { KeyHint.For("tab", "Next field"), KeyHint.For("ctrl+s", "Sign in"), KeyHint.For("ctrl+r", "Show password"), KeyHint.For("ctrl+t", "Theme"), KeyHint.For("escape", "Quit") };
            app.Bind("ctrl+t", () => { app.Theme = app.Theme.Name == "Dark" ? Theme.Light : TerminalTheme.Dark; _Heading.Content = Text.From("Sign in to ArgueBot", app.Theme.Text).Bold(); _Defaults.Content = Text.From(DefaultsText, app.Theme.Muted); });
            UiDispatcher dispatcher = new UiDispatcher(app, token);
            app.Post(() => _ = ShowAsync(app, dispatcher, token));
        }

        private async Task ShowAsync(TuiApplication app, UiDispatcher dispatcher, CancellationToken token)
        {
            Form form = new Form { SubmitLabel = "Sign in", CancelLabel = "Quit" };
            form.AddSection("Defaults: " + IdentitySeed.Email + " / " + IdentitySeed.Password + " / " + IdentitySeed.Token);
            TextField server = form.Add("Server URL", new TextField { Value = _Server, Placeholder = "http://127.0.0.1:4317" }, () => null);
            form.SetFieldHint(FieldIndex(form, server), "This machine's service, or another such as https://arguebot.example.com.");
            TextField email = form.Add("Email", new TextField { Value = _Email, Placeholder = IdentitySeed.Email });
            form.SetFieldHint(FieldIndex(form, email), "Default: " + IdentitySeed.Email);
            TextField password = form.Add("Password", new TextField { MaskChar = '*', RevealKey = KeyChord.Parse("ctrl+r") });
            form.SetFieldHint(FieldIndex(form, password), "Default: " + IdentitySeed.Password + ". Ctrl+R shows or hides it.");
            TextField bearer = form.Add("Or a bearer token", new TextField { MaskChar = '*', RevealKey = KeyChord.Parse("ctrl+r") });
            form.SetFieldHint(FieldIndex(form, bearer), "Instead of email and password. Default: " + IdentitySeed.Token);
            Checkbox insecure = form.AddCheckbox("Allow plain HTTP to a remote server (your password crosses the network unencrypted)", _AllowInsecureHttp);
            FormDialog<SignInForm> dialog = new FormDialog<SignInForm>("Sign in", form, () => new SignInForm { Server = server.Value.Trim(), Email = email.Value.Trim(), Password = password.Value, Token = bearer.Value.Trim(), AllowInsecureHttp = insecure.Checked }, app, value => SignInAsync(app, dispatcher, value, token));
            dialog.ApplyTheme(app.Theme);
            form.SetFocusedField(FieldIndex(form, _FocusServer || _Server.Length == 0 ? server : _Email.Length == 0 ? email : password));
            SignInForm? result = await app.ShowAsync<SignInForm>(dialog).ConfigureAwait(false);
            if (result == null && Client != null) { Client.Dispose(); Client = null; Session = null; }
            app.Quit();
        }

        private static int FieldIndex(Form form, object widget)
        {
            for (int index = 0; index < form.FieldCount; index++) if (ReferenceEquals(form.GetField(index), widget)) return index;
            return 0;
        }

        /// <summary>Check the server, sign in and remember the session. Throws with a message the form shows inline.</summary>
        internal async Task SignInAsync(TuiApplication? app, UiDispatcher? dispatcher, SignInForm value, CancellationToken token)
        {
            if (!Uri.TryCreate(value.Server, UriKind.Absolute, out Uri? address) || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.AbsolutePath.Trim('/').Length > 0)
                throw new InvalidOperationException("Enter a server URL such as http://127.0.0.1:4317 or https://arguebot.example.com, without a path.");
            if (value.Token.Length == 0 && (value.Email.Length == 0 || value.Password.Length == 0)) throw new InvalidOperationException("Enter an email and a password, or a bearer token.");
            string url = address.GetLeftPart(UriPartial.Authority);
            ArgueBotClient client;
            try { client = _Factory(url, value.AllowInsecureHttp); }
            catch (ArgumentException) when (!address.IsLoopback && address.Scheme == Uri.UriSchemeHttp)
            { throw new InvalidOperationException("This server uses plain HTTP, so your password would cross the network unencrypted. Use an https:// URL, or tick Allow plain HTTP if you trust this network."); }
            bool keep = false;
            try
            {
                HealthResponse health;
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    try { health = await client.SendAsync<HealthResponse>(HttpMethod.Get, "health", token: deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("No answer from " + url + " within 8 seconds. Check the address and that the service is running."); }
                    catch (HttpRequestException ex) { throw new InvalidOperationException("Cannot reach " + url + ". Check the address, that the ArgueBot service is running and your network. (" + ex.Message + ")"); }
                    catch (DomainException ex) when (ex.Code == "invalid_host") { throw new InvalidOperationException("The server does not accept the host name in this URL. Add it to the service's allowed hosts (ARGUEBOT_ALLOWED_HOSTS), or use another address for it."); }
                }
                if (!health.AuthenticationRequired) throw new InvalidOperationException("This server runs an ArgueBot version without sign-in. Update it to 2.1 or later.");
                TokenResponse? issued = null;
                if (value.Token.Length > 0) client.Token = value.Token;
                else
                {
                    string tenantId = "";
                    while (true)
                    {
                        try { issued = await client.LoginAsync(value.Email, value.Password, tenantId, token).ConfigureAwait(false); break; }
                        catch (TenantRequiredException ex)
                        {
                            if (app == null || dispatcher == null || tenantId.Length > 0) throw new InvalidOperationException("Your email and password work in several tenants. Choose one to continue.");
                            int choice = await dispatcher.InvokeAsync(() => app.SelectStyledAsync("Your email and password work in several tenants. Choose one", ex.Tenants.Select(item => item.Name).ToArray())).ConfigureAwait(false);
                            if (choice < 0) throw new InvalidOperationException("Choose a tenant to sign in.");
                            tenantId = ex.Tenants[choice].Id;
                        }
                    }
                }
                TokenDetails details;
                try { details = await client.TokenDetailsAsync(token).ConfigureAwait(false); }
                catch (DomainException ex) when (ex.StatusCode == 401 && value.Token.Length > 0) { throw new InvalidOperationException("That bearer token is not valid, has expired or is disabled (" + ex.Code + ")."); }
                TuiSession session = new TuiSession(_Home, url, value.AllowInsecureHttp, details, client.Token, issued?.ExpiresUtc ?? details.ExpiresUtc);
                session.Remember(client.Token, session.ExpiresUtc, true);
                Client?.Dispose();
                Client = client; Session = session; keep = true;
            }
            catch (DomainException ex)
            {
                throw new InvalidOperationException(ex.Code switch
                {
                    "invalid_credentials" => "The email or password is not correct.",
                    "credentials_required" => "Enter an email and a password, or a bearer token.",
                    _ => ex.Message
                });
            }
            finally { if (!keep) client.Dispose(); }
        }
    }
}
