namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class IdentityHttpChecks
    {
        internal static async Task RunAsync(ArgueBotClient owner, string endpoint, string home, CancellationToken token)
        {
            string email = "native-" + Guid.NewGuid().ToString("N") + "@example.invalid";
            UserRecord user = await owner.SendAsync<UserRecord>(HttpMethod.Post, "v1.0/api/users", new UserRequest { Name = "Native identity fixture", Email = email, Role = "operator", PasswordSha256 = new string('C', 64) }, token).ConfigureAwait(false);
            using (ArgueBotClient participant = LocalServiceConnector.CreateClient(endpoint, home))
            {
                await participant.LoginAsync(new LoginRequest { TenantId = "ten_local", Email = email, PasswordSha256 = new string('C', 64) }, token).ConfigureAwait(false);
                Conversation conversation = await participant.CreateConversationAsync("Native identity stream fixture", token).ConfigureAwait(false);
                UserPage found = await owner.SendAsync<UserPage>(HttpMethod.Get, "v1.0/api/users?search=" + Uri.EscapeDataString(email), token: token).ConfigureAwait(false);
                if (found.TotalCount != 1 || found.Items[0].Id != user.Id) throw new InvalidDataException("Public identity search did not decode its email filter exactly once.");
                await using (IAsyncEnumerator<DurableEvent> stream = participant.WatchEventsAsync(conversation.Id, conversation.LastEventSequence, token).GetAsyncEnumerator(token))
                {
                    Task<bool> waiting = stream.MoveNextAsync().AsTask();
                    await owner.SendAsync<UserRecord>(HttpMethod.Put, "v1.0/api/users/" + user.Id, new UserRequest { Name = user.Name, Email = email, Role = "operator", Active = false, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                    bool revoked = false;
                    try { await waiting.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); }
                    catch (DomainException exception) when (exception.StatusCode == 401) { revoked = true; }
                    if (!revoked) throw new InvalidDataException("Disabling a user did not terminate its active authenticated event stream.");
                }
            }
        }
    }
}
