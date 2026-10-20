namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class UserAssignmentHttpChecks
    {
        internal static async Task RunAsync(ArgueBotClient owner, ArgueBotClient participant, CancellationToken token)
        {
            RequestContext principal = await participant.SendAsync<RequestContext>(HttpMethod.Get, "v1.0/api/auth/me", token: token).ConfigureAwait(false);
            string root = "v1.0/api/users/" + principal.UserId + "/role-assignments";
            UserRoleAssignmentRequest request = new UserRoleAssignmentRequest { RoleName = "viewer", ResourceScope = "Resource", ResourceId = "scope_" + Guid.NewGuid().ToString("N") };
            long initialCount = (await owner.SendAsync<UserRoleAssignmentPage>(HttpMethod.Get, root, token: token).ConfigureAwait(false)).TotalCount;
            UserRoleAssignmentRecord assignment = await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Post, root, request, token).ConfigureAwait(false);
            try
            {
                if (assignment.RoleName != "Viewer" || assignment.UserId != principal.UserId || assignment.TenantId != principal.TenantId || assignment.Protected) throw new InvalidDataException("HTTP assignment lost canonical role or principal scope.");
                UserRoleAssignmentPage page = await owner.SendAsync<UserRoleAssignmentPage>(HttpMethod.Get, root + "?pageSize=1", token: token).ConfigureAwait(false);
                if (page.TotalCount != initialCount + 1 || page.Items.Count != 1 || page.Items[0].UserId != principal.UserId) throw new InvalidDataException("HTTP assignment paging lost its selected principal.");
                await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Get, root + "/" + assignment.Id, token: token).ConfigureAwait(false);
                await DeniedAsync(async () => { await participant.SendAsync<UserRoleAssignmentPage>(HttpMethod.Get, root, token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await participant.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Post, root, request, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Post, "v1.0/api/users/usr_local/role-assignments", request, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Get, "v1.0/api/users/usr_local/role-assignments/" + assignment.Id, token: token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                request.ResourceScope = "Tenant"; request.ResourceId = ""; request.InheritsToChildren = true; request.ExpectedRevision = assignment.Revision;
                assignment = await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Put, root + "/" + assignment.Id, request, token).ConfigureAwait(false);
                if (!assignment.InheritsToChildren || assignment.Revision != 2) throw new InvalidDataException("HTTP scope update lost inheritance or revision.");
                request.Active = false; request.ExpectedRevision = assignment.Revision;
                assignment = await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Put, root + "/" + assignment.Id, request, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Put, root + "/" + assignment.Id, request, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                request.ResourceScope = "0";
                await DeniedAsync(async () => { await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Post, root, request, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
            }
            finally
            {
                if (assignment.Active) await owner.SendAsync<UserRoleAssignmentRecord>(HttpMethod.Put, root + "/" + assignment.Id, new UserRoleAssignmentRequest { RoleName = assignment.RoleName, ResourceScope = assignment.ResourceScope, ResourceId = assignment.ResourceId, InheritsToChildren = assignment.InheritsToChildren, Active = false, ExpectedRevision = assignment.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("HTTP scoped assignment did not enforce status " + expected + ".");
        }
    }
}
