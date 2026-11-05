namespace ArgueBot.Core.Services
{
    using System;

    /// <summary>
    /// The access class every authenticated route needs. Public routes are registered before authentication
    /// (health, dashboard assets, contract, sign-in, sign-in page information and public share links) and never reach this map.
    /// </summary>
    public static class RouteAccess
    {
        /// <summary>No sign-in: health, dashboard assets, contract documents, sign-in, sign-in page information and public share links.</summary>
        public const string Public = "Public";
        /// <summary>Any signed-in user of an active tenant.</summary>
        public const string Member = "Member";
        /// <summary>A tenant administrator (or system administrator) of the caller's tenant.</summary>
        public const string TenantAdmin = "TenantAdmin";
        /// <summary>A system administrator (users with isAdmin).</summary>
        public const string SystemAdmin = "SystemAdmin";

        /// <summary>Classify an authenticated route by method and path (without the query).</summary>
        public static string Classify(string method, string path)
        {
            ArgumentNullException.ThrowIfNull(method); ArgumentNullException.ThrowIfNull(path);
            string route = path.Length > 1 ? path.TrimEnd('/') : path;
            if (Under(route, "/v1.0/api/settings") || Under(route, "/v2.0/api/settings") || Under(route, "/v2.0/api/tenants") || Under(route, "/v2.0/api/configuration/credential-keys") || Under(route, "/v2.0/api/configuration/secret-keys")) return SystemAdmin;
            if (Under(route, "/v1.0/api/request-history") || Under(route, "/v2.0/api/request-history") || Under(route, "/v2.0/api/users") || Under(route, "/v2.0/api/audit") || Under(route, "/v2.0/api/sessions")) return TenantAdmin;
            return Member;
        }

        /// <summary>True when a caller with these flags may use a route of this access class.</summary>
        public static bool Permits(string access, bool isAdmin, bool isTenantAdmin)
        {
            if (access == SystemAdmin) return isAdmin;
            if (access == TenantAdmin) return isAdmin || isTenantAdmin;
            return true;
        }

        private static bool Under(string path, string root) { return path == root || path.StartsWith(root + "/", StringComparison.Ordinal); }
    }
}
