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

    internal static class RoleHttpChecks
    {
        internal static async Task RunAsync(ArgueBotClient owner, ArgueBotClient participant, CancellationToken token)
        {
            RolePage page = await owner.SendAsync<RolePage>(HttpMethod.Get, "v1.0/api/roles?search=Viewer", token: token).ConfigureAwait(false);
            if (page.TotalCount != 1 || !page.Items[0].Builtin || !page.Items[0].Protected) throw new InvalidDataException("HTTP role metadata lost built-in protection.");
            string name = "Native role +%議論 " + Guid.NewGuid().ToString("N");
            RoleRecord role = await owner.SendAsync<RoleRecord>(HttpMethod.Post, "v1.0/api/roles", new RoleRequest { Name = name, CloneRoleId = page.Items[0].Id }, token).ConfigureAwait(false);
            try
            {
                RolePage found = await owner.SendAsync<RolePage>(HttpMethod.Get, "v1.0/api/roles?search=" + Uri.EscapeDataString(name) + "&pageSize=1", token: token).ConfigureAwait(false);
                if (found.TotalCount != 1 || found.Items.Count != 1 || found.Items[0].Id != role.Id) throw new InvalidDataException("HTTP role filters lost literal punctuation or Unicode.");
                await DeniedAsync(async () => { await participant.SendAsync<RolePage>(HttpMethod.Get, "v1.0/api/roles", token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await participant.SendAsync<RoleRecord>(HttpMethod.Post, "v1.0/api/roles", new RoleRequest { Name = "Forbidden role" }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<RoleRecord>(HttpMethod.Put, "v1.0/api/roles/" + page.Items[0].Id, new RoleRequest { Name = "Viewer", ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                role = await PermissionHttpChecks.RunAsync(owner, participant, role, token).ConfigureAwait(false);
                RoleRecord read = await owner.SendAsync<RoleRecord>(HttpMethod.Get, "v1.0/api/roles/" + role.Id, token: token).ConfigureAwait(false);
                if (read.TenantId != "ten_local" || read.Builtin || read.Protected) throw new InvalidDataException("HTTP custom role inherited protected/global metadata.");
                role = await owner.SendAsync<RoleRecord>(HttpMethod.Put, "v1.0/api/roles/" + role.Id, new RoleRequest { Name = role.Name, Active = false, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<RoleRecord>(HttpMethod.Put, "v1.0/api/roles/" + role.Id, new RoleRequest { Name = role.Name, ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                await DeniedAsync(async () => { await owner.SendAsync<RolePage>(HttpMethod.Get, "v1.0/api/roles?pageSize=101", token: token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
            }
            finally
            {
                if (role.Active) await owner.SendAsync<RoleRecord>(HttpMethod.Put, "v1.0/api/roles/" + role.Id, new RoleRequest { Name = role.Name, Active = false, ExpectedRevision = role.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("HTTP role control did not enforce status " + expected + ".");
        }
    }
}
