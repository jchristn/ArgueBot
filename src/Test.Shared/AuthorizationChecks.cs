namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Security;

    internal static class AuthorizationChecks
    {
        internal static Task RunAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            AuthorizationPrincipal user = new AuthorizationPrincipal { Id = "usr_editor", TenantId = "ten_fixture", IsAuthenticated = true, IsActive = true };
            AuthorizationTarget target = new AuthorizationTarget { TenantId = "ten_fixture", ResourceType = "Conversation", ResourceId = "cnv_one", OwnerUserId = user.Id, Operation = "Read" };
            AuthorizationGrant read = Grant("grant_read", "Read");
            Expect(AuthorizationEvaluator.Evaluate(user, target, Array.Empty<AuthorizationGrant>()), false, "implicit_deny");
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), true, "permitted");
            AuthorizationGrant deny = Grant("grant_deny", "Read", AuthorizationEffect.Deny);
            foreach (AuthorizationGrant[] order in new[] { new[] { read, deny }, new[] { deny, read } }) Expect(AuthorizationEvaluator.Evaluate(user, target, order), false, "explicit_deny");
            deny.Active = false; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read, deny }), true, "permitted"); deny.Active = true;
            target.Operation = "Write";
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { Grant("create", "Create"), Grant("update", "Update") }), false, "implicit_deny");
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { Grant("create", "Create"), Grant("update", "Update"), Grant("delete", "Delete") }), true, "permitted");
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { Grant("write", "Write"), Grant("deny_delete", "Delete", AuthorizationEffect.Deny) }), false, "explicit_deny");
            foreach (string operation in new[] { "Execute", "Admin" }) { target.Operation = operation; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { Grant("write", "Write") }), false, "implicit_deny"); }
            target.Operation = "Read";
            read.Scope = AuthorizationScope.Resource; read.InheritsToChildren = false; read.ResourceId = "cnv_one";
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), true, "permitted");
            target.ResourceId = "cnv_ONE"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "implicit_deny"); target.ResourceId = "cnv_one";
            read = Grant("tenant_metadata", "Read"); read.InheritsToChildren = false;
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "implicit_deny");
            target.ResourceId = ""; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "implicit_deny");
            target.ResourceType = "Tenant"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), true, "permitted");
            target.ResourceType = "Conversation"; target.ResourceId = "cnv_one"; read.InheritsToChildren = true; read.RequiresOwnership = true;
            target.OwnerUserId = "usr_other"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "implicit_deny"); target.OwnerUserId = user.Id;
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), true, "permitted");
            read.TenantId = "ten_other"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "implicit_deny"); read.TenantId = target.TenantId;
            target.TenantId = "ten_other"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "tenant_conflict"); target.TenantId = user.TenantId;
            AuthorizationPrincipal credential = new AuthorizationPrincipal { Id = "cre_fixture", Kind = AuthorizationPrincipalKind.Credential, OwnerUserId = user.Id, TenantId = user.TenantId, IsAuthenticated = true, IsActive = true };
            Expect(AuthorizationEvaluator.Evaluate(credential, target, new[] { Grant("credential_read", "Read") }, Array.Empty<AuthorizationGrant>()), false, "owner_ceiling_implicit_deny");
            AuthorizationDecision permitted = AuthorizationEvaluator.Evaluate(credential, target, new[] { Grant("credential_read", "Read") }, new[] { read });
            Expect(permitted, true, "permitted");
            if (!permitted.OwnerCeilingApplied || permitted.UsedAdminBypass || permitted.MatchedGrantIds.Count != 2) throw new InvalidDataException("Credential decisions lost their owner ceiling or provenance.");
            Expect(AuthorizationEvaluator.Evaluate(credential, target, new[] { Grant("credential_read", "Read") }, new[] { read, deny }), false, "owner_ceiling_explicit_deny");
            credential.IsAdmin = true; credential.IsTenantAdmin = true;
            Expect(AuthorizationEvaluator.Evaluate(credential, target, Array.Empty<AuthorizationGrant>()), false, "implicit_deny");
            permitted = AuthorizationEvaluator.Evaluate(credential, target, new[] { Grant("credential_read", "Read") });
            if (!permitted.Allowed || permitted.UsedAdminBypass || !permitted.OwnerCeilingUsedAdminBypass) throw new InvalidDataException("An owner administrator flag was inherited as credential bypass.");
            target.IsPlatform = true; target.ResourceType = "Service"; target.Operation = "Admin";
            Expect(AuthorizationEvaluator.Evaluate(credential, target, new[] { Grant("credential_all", "All") }), false, "platform_forbidden");
            user.IsTenantAdmin = true; Expect(AuthorizationEvaluator.Evaluate(user, target, Array.Empty<AuthorizationGrant>()), false, "platform_forbidden");
            user.IsAdmin = true; permitted = AuthorizationEvaluator.Evaluate(user, target, new[] { deny });
            if (!permitted.Allowed || !permitted.UsedAdminBypass) throw new InvalidDataException("An explicit platform administrator bypass was not reported.");
            user.IsActive = false; Expect(AuthorizationEvaluator.Evaluate(user, target, Array.Empty<AuthorizationGrant>()), false, "inactive"); user.IsActive = true;
            user.IsAuthenticated = false; Expect(AuthorizationEvaluator.Evaluate(user, target, Array.Empty<AuthorizationGrant>()), false, "unauthenticated"); user.IsAuthenticated = true;
            user.IsAdmin = false; user.IsTenantAdmin = false; target.IsPlatform = false; target.ResourceType = "Conversation"; target.Operation = "Read";
            AuthorizationGrant malformed = Grant("malformed", "Read"); malformed.Effect = (AuthorizationEffect)99;
            Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read, malformed }), false, "invalid_grant");
            Expect(AuthorizationEvaluator.Evaluate(user, target, Enumerable.Repeat(read, AuthorizationEvaluator.MaximumGrants + 1).ToList()), false, "grant_limit_exceeded");
            target.Operation = "All"; Expect(AuthorizationEvaluator.Evaluate(user, target, new[] { read }), false, "invalid_target");
            return Task.CompletedTask;
        }
        private static AuthorizationGrant Grant(string id, string operation, AuthorizationEffect effect = AuthorizationEffect.Permit)
        {
            return new AuthorizationGrant { Id = id, TenantId = "ten_fixture", ResourceTypes = new List<string> { "All" }, Operations = new List<string> { operation }, Effect = effect, InheritsToChildren = true };
        }
        private static void Expect(AuthorizationDecision decision, bool allowed, string reason)
        { if (decision.Allowed != allowed || decision.Reason != reason) throw new InvalidDataException("Permission evaluation expected " + allowed + "/" + reason + " but returned " + decision.Allowed + "/" + decision.Reason + "."); }
    }
}
