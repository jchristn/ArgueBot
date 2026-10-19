namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class PermissionHttpChecks
    {
        internal static async Task<RoleRecord> RunAsync(ArgueBotClient owner, ArgueBotClient participant, RoleRecord role, CancellationToken token)
        {
            string name = "Native permission %+議論 " + Guid.NewGuid().ToString("N");
            PermissionRequest request = new PermissionRequest { Name = name, ResourceTypes = new List<string> { "Conversation" }, Operations = new List<string> { "Read" } };
            PermissionRecord permission = await owner.SendAsync<PermissionRecord>(HttpMethod.Post, "v1.0/api/permissions", request, token).ConfigureAwait(false);
            try
            {
                PermissionPage found = await owner.SendAsync<PermissionPage>(HttpMethod.Get, "v1.0/api/permissions?search=" + Uri.EscapeDataString(name), token: token).ConfigureAwait(false);
                if (found.TotalCount != 1 || found.Items[0].Id != permission.Id) throw new InvalidDataException("HTTP permission search lost literal filtering.");
                PermissionRecord read = await owner.SendAsync<PermissionRecord>(HttpMethod.Get, "v1.0/api/permissions/" + permission.Id, token: token).ConfigureAwait(false);
                if (read.TenantId != "ten_local" || read.Protected || read.Effect != "Permit") throw new InvalidDataException("HTTP permission metadata lost its tenant or effect.");
                await DeniedAsync(async () => { await participant.SendAsync<PermissionPage>(HttpMethod.Get, "v1.0/api/permissions", token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await participant.SendAsync<PermissionRecord>(HttpMethod.Post, "v1.0/api/permissions", request, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                string mappingPath = "v1.0/api/roles/" + role.Id + "/permissions";
                RolePermissionPage baseline = await owner.SendAsync<RolePermissionPage>(HttpMethod.Get, mappingPath, token: token).ConfigureAwait(false);
                PermissionRecord protectedPermission = baseline.Items.Single().Permission;
                await DeniedAsync(async () => { await owner.SendAsync<PermissionRecord>(HttpMethod.Put, "v1.0/api/permissions/" + protectedPermission.Id, request, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await participant.SendAsync<RoleRecord>(HttpMethod.Put, mappingPath, new RolePermissionsRequest { ExpectedRevision = role.Revision }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                role = await owner.SendAsync<RoleRecord>(HttpMethod.Put, mappingPath, new RolePermissionsRequest { PermissionIds = new List<string> { permission.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                RolePermissionPage current = await owner.SendAsync<RolePermissionPage>(HttpMethod.Get, mappingPath + "?pageSize=1", token: token).ConfigureAwait(false);
                if (current.TotalCount != 1 || current.RoleRevision != role.Revision || current.Items[0].PermissionId != permission.Id) throw new InvalidDataException("HTTP role mappings lost replacement state or current revision.");
                role = await owner.SendAsync<RoleRecord>(HttpMethod.Put, mappingPath, new RolePermissionsRequest { ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                RolePermissionPage inactive = await owner.SendAsync<RolePermissionPage>(HttpMethod.Get, mappingPath + "?active=false", token: token).ConfigureAwait(false);
                if (!inactive.Items.Any(mapping => mapping.PermissionId == permission.Id && !mapping.Active)) throw new InvalidDataException("HTTP replacement lost inactive mapping history.");
                request.Active = false; request.ExpectedRevision = permission.Revision;
                permission = await owner.SendAsync<PermissionRecord>(HttpMethod.Put, "v1.0/api/permissions/" + permission.Id, request, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<PermissionRecord>(HttpMethod.Put, "v1.0/api/permissions/" + permission.Id, request, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                request.Effect = "1";
                await DeniedAsync(async () => { await owner.SendAsync<PermissionRecord>(HttpMethod.Post, "v1.0/api/permissions", request, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                return role;
            }
            finally
            {
                if (permission.Active)
                {
                    request.Effect = permission.Effect; request.Active = false; request.ExpectedRevision = permission.Revision;
                    await owner.SendAsync<PermissionRecord>(HttpMethod.Put, "v1.0/api/permissions/" + permission.Id, request, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("HTTP permission management did not enforce status " + expected + ".");
        }
    }
}
