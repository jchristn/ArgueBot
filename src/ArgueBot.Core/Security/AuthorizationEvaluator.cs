namespace ArgueBot.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Pure scoped permission evaluation. Store/transport resolvers must supply current authenticated facts and active expanded grants.</summary>
    public static class AuthorizationEvaluator
    {
        /// <summary>Maximum expanded grants per principal or owner evaluation. Excess input fails closed.</summary>
        public const int MaximumGrants = 4096;
        private static readonly string[] _Mutations = { "Create", "Update", "Delete" };
        private static readonly string[] _Operations = { "Read", "Create", "Update", "Delete", "Write", "Execute", "Admin", "All" };

        /// <summary>Evaluate user or credential grants. Credential authority is always intersected with its owner, never inherited directly from owner flags.</summary>
        public static AuthorizationDecision Evaluate(AuthorizationPrincipal principal, AuthorizationTarget target, IReadOnlyList<AuthorizationGrant> grants, IReadOnlyList<AuthorizationGrant>? ownerGrants = null)
        {
            ArgumentNullException.ThrowIfNull(principal); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(grants);
            if (!principal.IsAuthenticated) return Deny("unauthenticated");
            if (!principal.IsActive) return Deny("inactive");
            if (!ValidPrincipal(principal)) return Deny("invalid_principal");
            if (!ValidTarget(target)) return Deny("invalid_target");
            bool credential = principal.Kind == AuthorizationPrincipalKind.Credential;
            bool root = !credential && (principal.Kind == AuthorizationPrincipalKind.Administrator || principal.IsAdmin);
            if (!target.IsPlatform && !root && principal.TenantId != target.TenantId) return Deny("tenant_conflict");
            if (target.IsPlatform && !root) return Deny("platform_forbidden");
            if (root || (!credential && principal.IsTenantAdmin)) return new AuthorizationDecision { Allowed = true, Reason = "admin_bypass", UsedAdminBypass = true };
            if (grants.Count > MaximumGrants || (credential && ownerGrants != null && ownerGrants.Count > MaximumGrants)) return Deny("grant_limit_exceeded");
            AuthorizationDecision decision = EvaluateGrants(principal, target, grants);
            if (!credential) return decision;
            decision.OwnerCeilingApplied = true;
            if (!decision.Allowed) return decision;
            AuthorizationPrincipal owner = new AuthorizationPrincipal { Id = principal.OwnerUserId, TenantId = principal.TenantId, Kind = AuthorizationPrincipalKind.User, IsAuthenticated = true, IsActive = true, IsAdmin = principal.IsAdmin, IsTenantAdmin = principal.IsTenantAdmin };
            AuthorizationDecision ceiling = Evaluate(owner, target, ownerGrants ?? Array.Empty<AuthorizationGrant>());
            decision.OwnerCeilingUsedAdminBypass = ceiling.UsedAdminBypass;
            if (!ceiling.Allowed)
            {
                decision.Allowed = false; decision.Reason = "owner_ceiling_" + ceiling.Reason;
            }
            decision.MatchedGrantIds = decision.MatchedGrantIds.Concat(ceiling.MatchedGrantIds).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
            return decision;
        }
        private static AuthorizationDecision EvaluateGrants(AuthorizationPrincipal principal, AuthorizationTarget target, IReadOnlyList<AuthorizationGrant> grants)
        {
            List<AuthorizationGrant> relevant = new List<AuthorizationGrant>();
            foreach (AuthorizationGrant grant in grants)
            {
                if (grant == null) return Deny("invalid_grant");
                if (!grant.Active) continue;
                if (!ValidId(grant.TenantId)) return Deny("invalid_grant");
                if (grant.TenantId != target.TenantId) continue;
                if (!IsValidGrant(grant)) return Deny("invalid_grant");
                relevant.Add(grant);
            }
            HashSet<string> permits = new HashSet<string>(StringComparer.Ordinal); HashSet<string> denies = new HashSet<string>(StringComparer.Ordinal);
            bool missing = false;
            IEnumerable<string> operations = Equal(target.Operation, "Write") ? _Mutations : new[] { target.Operation };
            foreach (string operation in operations)
            {
                bool permitted = false;
                foreach (AuthorizationGrant grant in relevant)
                {
                    if (!Matches(principal, target, operation, grant)) continue;
                    if (grant.Effect == AuthorizationEffect.Deny) denies.Add(grant.Id);
                    else { permits.Add(grant.Id); permitted = true; }
                }
                if (!permitted) missing = true;
            }
            if (denies.Count > 0) return new AuthorizationDecision { Reason = "explicit_deny", MatchedGrantIds = denies.OrderBy(id => id, StringComparer.Ordinal).ToList() };
            return new AuthorizationDecision { Allowed = !missing, Reason = missing ? "implicit_deny" : "permitted", MatchedGrantIds = permits.OrderBy(id => id, StringComparer.Ordinal).ToList() };
        }
        private static bool Matches(AuthorizationPrincipal principal, AuthorizationTarget target, string operation, AuthorizationGrant grant)
        {
            if (!grant.ResourceTypes.Any(type => Equal(type, "All") || Equal(type, target.ResourceType))) return false;
            if (!grant.Operations.Any(value => Equal(value, "All") || Equal(value, operation) || (Equal(value, "Write") && _Mutations.Any(mutation => Equal(mutation, operation))))) return false;
            if (grant.Scope == AuthorizationScope.Resource)
            {
                if (target.ResourceId.Length == 0 || grant.ResourceId != target.ResourceId) return false;
            }
            else if (!grant.InheritsToChildren && !(Equal(target.ResourceType, "Tenant") && (target.ResourceId.Length == 0 || target.ResourceId == target.TenantId))) return false;
            string user = principal.Kind == AuthorizationPrincipalKind.Credential ? principal.OwnerUserId : principal.Id;
            return !grant.RequiresOwnership || (target.OwnerUserId.Length > 0 && target.OwnerUserId == user);
        }
        private static bool ValidPrincipal(AuthorizationPrincipal principal)
        {
            if (!Enum.IsDefined(principal.Kind) || !ValidId(principal.Id)) return false;
            if (principal.Kind == AuthorizationPrincipalKind.Administrator) return principal.IsAdmin;
            if (!ValidId(principal.TenantId)) return false;
            return principal.Kind != AuthorizationPrincipalKind.Credential || ValidId(principal.OwnerUserId);
        }
        private static bool ValidTarget(AuthorizationTarget target)
        {
            return (target.IsPlatform || ValidId(target.TenantId)) && ValidWord(target.ResourceType) && !Equal(target.ResourceType, "All") && target.Operation != null && _Operations.Any(operation => !Equal(operation, "All") && Equal(operation, target.Operation)) && OptionalId(target.ResourceId) && OptionalId(target.OwnerUserId);
        }
        internal static string? CanonicalOperation(string value) { return _Operations.FirstOrDefault(operation => Equal(operation, value)); }
        internal static bool IsValidGrant(AuthorizationGrant grant)
        {
            if (!ValidId(grant.Id) || !Enum.IsDefined(grant.Effect) || !Enum.IsDefined(grant.Scope) || grant.ResourceTypes == null || grant.Operations == null || grant.ResourceTypes.Count < 1 || grant.ResourceTypes.Count > 32 || grant.Operations.Count < 1 || grant.Operations.Count > 16 || !grant.ResourceTypes.All(ValidWord) || !grant.Operations.All(value => _Operations.Any(operation => Equal(operation, value)))) return false;
            if (grant.Scope == AuthorizationScope.Resource) return ValidId(grant.ResourceId) && !grant.InheritsToChildren;
            return grant.ResourceId == "";
        }
        private static bool OptionalId(string? value) { return value != null && (value.Length == 0 || ValidId(value)); }
        private static bool ValidId(string? value) { return !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsWhiteSpace); }
        private static bool ValidWord(string? value) { return !string.IsNullOrEmpty(value) && value.Length <= 64 && char.IsAsciiLetter(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'); }
        private static bool Equal(string? left, string? right) { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static AuthorizationDecision Deny(string reason) { return new AuthorizationDecision { Reason = reason }; }
    }
}
