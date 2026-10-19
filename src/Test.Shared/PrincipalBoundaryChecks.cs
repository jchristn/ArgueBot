namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class PrincipalBoundaryChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, RequestContext owner, CancellationToken token)
        {
            RunnerProfile profile = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Principal boundary fixture", Kind = "cli", Provider = "plain", Executable = "fixture" }, token).ConfigureAwait(false);
            Conversation conversation = await database.CreateConversationAsync(owner, "Principal boundary fixture", token).ConfigureAwait(false);
            try
            {
                foreach (RequestContext invalid in new[]
                {
                    new RequestContext { TenantId = owner.TenantId, UserId = owner.UserId, Role = "admin", IsAdmin = true },
                    new RequestContext { IsAuthenticated = true, UserId = owner.UserId, Role = "admin" },
                    new RequestContext { IsAuthenticated = true, TenantId = owner.TenantId, Role = "admin" }
                })
                {
                    List<Func<Task>> reads = new List<Func<Task>>
                    {
                        async () => { await database.EnumerateConversationsAsync(invalid, token: token).ConfigureAwait(false); },
                        async () => { await database.ReadConversationAsync(invalid, conversation.Id, token).ConfigureAwait(false); },
                        async () => { await database.Profiles.EnumerateAsync(invalid, token).ConfigureAwait(false); },
                        async () => { await database.Profiles.ReadAsync(invalid, profile.Id, token).ConfigureAwait(false); },
                        async () => { await database.ReadMessagePageAsync(invalid, conversation.Id, token: token).ConfigureAwait(false); },
                        async () => { await database.ReadEventsAsync(invalid, conversation.Id, token: token).ConfigureAwait(false); },
                        async () => { await database.CreateConversationAsync(invalid, "Rejected", token).ConfigureAwait(false); }
                    };
                    foreach (Func<Task> read in reads)
                    {
                        bool denied = false;
                        try { await read().ConfigureAwait(false); }
                        catch (DomainException exception) when (exception.StatusCode == 401 && exception.Code == "unauthenticated") { denied = true; }
                        if (!denied) throw new InvalidDataException("A domain method accepted an unauthenticated or unbound principal.");
                    }
                }
                RequestContext viewer = new RequestContext { IsAuthenticated = true, TenantId = owner.TenantId, UserId = owner.UserId, Role = "viewer" };
                if ((await database.ReadConversationAsync(viewer, conversation.Id, token).ConfigureAwait(false)).Id != conversation.Id || (await database.Profiles.ReadAsync(viewer, profile.Id, token).ConfigureAwait(false)).Id != profile.Id) throw new InvalidDataException("The principal guard rejected an authenticated read-only owner.");
            }
            finally
            {
                await database.DeleteConversationAsync(owner, conversation.Id, CancellationToken.None).ConfigureAwait(false);
                await database.Profiles.DeleteAsync(owner, profile.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
