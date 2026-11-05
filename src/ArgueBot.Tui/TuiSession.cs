namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;

    /// <summary>The signed-in identity for one workspace run, and how to remember a refreshed session.</summary>
    internal sealed class TuiSession
    {
        internal TuiSession(string home, string server, bool allowInsecureHttp, TokenDetails details, string token, DateTime? expiresUtc)
        { Home = home; Server = server; AllowInsecureHttp = allowInsecureHttp; Details = details; Token = token; ExpiresUtc = expiresUtc; }
        internal string Home { get; }
        internal string Server { get; }
        internal bool AllowInsecureHttp { get; }
        internal TokenDetails Details { get; set; }
        internal string Token { get; private set; }
        internal DateTime? ExpiresUtc { get; private set; }
        /// <summary>False when the token came from ARGUEBOT_TOKEN, which is never written to disk.</summary>
        internal bool Persist { get; set; } = true;
        internal bool IsSession { get { return Token.StartsWith(SessionProtector.Prefix, StringComparison.Ordinal); } }
        internal bool IsTenantAdmin { get { return Details.User.IsAdmin || Details.User.IsTenantAdmin; } }
        internal bool IsSystemAdmin { get { return Details.User.IsAdmin; } }
        internal string Describe()
        {
            string name = (Details.User.FirstName + " " + Details.User.LastName).Trim();
            return (name.Length == 0 ? Details.User.Email : name) + " | " + Details.Tenant.Name + " | " + Server;
        }
        /// <summary>User and tenant for the connection line, which has room for them at any width.</summary>
        internal string ShortIdentity()
        {
            string name = (Details.User.FirstName + " " + Details.User.LastName).Trim();
            return (name.Length == 0 ? Details.User.Email : name) + " in " + Details.Tenant.Name;
        }
        /// <summary>Remember the session (never the password) for the next start, unless it came from ARGUEBOT_TOKEN.</summary>
        internal void Remember(string token, DateTime? expiresUtc, bool persist)
        {
            Token = token; ExpiresUtc = expiresUtc;
            if (persist) ClientSessionStore.Save(Home, new ClientSession { Server = Server, Token = token, ExpiresUtc = expiresUtc, Email = Details.User.Email, TenantName = Details.Tenant.Name, AllowInsecureHttp = AllowInsecureHttp });
        }
    }
}
