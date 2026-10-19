namespace ArgueBot.Server
{
    using System;
    using System.Collections.Specialized;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class SessionAuthentication
    {
        private static readonly string[] _Unsupported = { "x-email", "x-password", "x-access-key", "x-secret-key", "x-signature", "x-date", "x-nonce", "x-session-token", "x-admin-api-key", "x-api-key" };

        internal static string Read(NameValueCollection headers)
        {
            foreach (string name in _Unsupported)
                if (headers.GetValues(name) != null) throw new DomainException(401, "unsupported_auth", "This local listener accepts enrolled sessions only. Remove other authentication material.");
            string bearer = Single(headers, "Authorization");
            string alternate = Single(headers, "x-token");
            if (bearer.Length > 0 && !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new DomainException(401, "unsupported_auth", "Use an enrolled Bearer or x-token session.");
            string secret = bearer.Length > 0 ? bearer.Substring(7) : alternate;
            if ((bearer.Length > 0 && secret.Length == 0) || secret.Length > 16384 || secret.IndexOfAny(new[] { ' ', '\t', '\r', '\n', ',' }) >= 0) throw new DomainException(401, "invalid_auth", "Supply one complete session token.");
            if (alternate.Length > 0 && secret != alternate) throw new DomainException(401, "conflicting_auth", "Token carriers disagree.");
            return secret;
        }

        internal static void BindTenant(NameValueCollection headers, RequestContext context)
        {
            foreach (string name in new[] { "x-tenant-guid", "x-tenant-id" })
            {
                string hint = Single(headers, name);
                if (hint.Length > 0 && hint != context.TenantId) throw new DomainException(403, "tenant_conflict", "Tenant hints disagree with the authenticated session.");
            }
        }

        private static string Single(NameValueCollection headers, string name)
        {
            string[]? values = headers.GetValues(name);
            if (values == null) return "";
            if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0].Contains(',', StringComparison.Ordinal)) throw new DomainException(401, "ambiguous_auth", "Authentication and tenant headers must occur exactly once with a nonempty value.");
            return values[0];
        }
    }
}
