namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class ExecutionIdentityChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, ArgueBotPaths paths, CancellationToken token)
        {
            RequestContext owner = new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local", IsAdmin = true, Role = "admin" };
            UserRecord user = await database.Users.CreateAsync(owner, new UserRequest { Name = "Execution identity fixture", Email = "execution-" + Guid.NewGuid().ToString("N") + "@example.invalid", Role = "operator", PasswordSha256 = new string('E', 64) }, token).ConfigureAwait(false);
            SessionMethods sessions = new SessionMethods(database, new TokenProtector(paths));
            string secret = await sessions.LoginAsync(new LoginRequest { TenantId = user.TenantId, Email = user.Email, PasswordSha256 = new string('E', 64) }, token: token).ConfigureAwait(false);
            RequestContext context = await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false) ?? throw new InvalidDataException("No execution principal.");
            RunnerProfile? profile = null; Conversation? conversation = null;
            try
            {
                profile = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Execution authorization fixture", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
                conversation = await database.CreateConversationAsync(context, "Execution authorization fixture", token).ConfigureAwait(false);
                FixtureRunner runner = new FixtureRunner { Wait = true };
                await using (ConversationEngine engine = new ConversationEngine(database, runner))
                {
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Keep accepted work independent of its issuing session", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "identity-run" }, token).ConfigureAwait(false);
                    await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    await sessions.RevokeAsync(secret, token).ConfigureAwait(false);
                    await Task.Delay(500, token).ConfigureAwait(false);
                    if ((await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false)).State != "agent_turn") throw new InvalidDataException("Signing out cancelled service-owned accepted work.");
                    user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "viewer", ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                    await PausedAsync(database, context, conversation.Id, run.Id, token).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                    GenerationAttempt attempt = (await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single();
                    if (run.Outcome != "authorization_revoked" || attempt.State != "interrupted" || attempt.ErrorCode != "authorization_revoked" || runner.Calls != 1) throw new InvalidDataException("Role revocation did not interrupt generation and preserve its provenance.");
                    AuditPage audit = await database.Audit.EnumerateAsync(owner, "run.authorization", token: token).ConfigureAwait(false);
                    if (!audit.Items.Any(entry => entry.ResourceId == run.Id && entry.UserId == user.Id && entry.ErrorCode == "authorization_revoked")) throw new InvalidDataException("Background execution denial was not audited.");
                    await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "stale-context-resume" }, "fixture", token).ConfigureAwait(false);
                    await PausedAsync(database, context, conversation.Id, run.Id, token).ConfigureAwait(false);
                    if (runner.Calls != 1 || (await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false)).TotalCount != 1) throw new InvalidDataException("A stale accepted context created another attempt after execution was revoked.");
                    user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "operator", Active = false, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                    if (await database.Users.CanExecuteAsync(context, token).ConfigureAwait(false)) throw new InvalidDataException("An inactive operator retained execution authority.");
                    await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = "fixture-cleanup-stop" }, "fixture", token).ConfigureAwait(false);
                    user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "operator", Active = true, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                    secret = await sessions.LoginAsync(new LoginRequest { TenantId = user.TenantId, Email = user.Email, PasswordSha256 = new string('E', 64) }, token: token).ConfigureAwait(false);
                    context = await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false) ?? throw new InvalidDataException("The explicitly re-enabled operator could not log in.");
                    DebateRun second = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Disable this account while generation is active", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "identity-disable-run" }, token).ConfigureAwait(false);
                    using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(10));
                        while (runner.Calls < 2) await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                    }
                    user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "operator", Active = false, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                    await PausedAsync(database, context, conversation.Id, second.Id, token).ConfigureAwait(false);
                    second = await database.Runs.ReadAsync(context, conversation.Id, second.Id, token).ConfigureAwait(false);
                    if (second.Outcome != "authorization_revoked" || runner.Calls != 2) throw new InvalidDataException("Disabling an active operator did not interrupt its current generation.");
                    await engine.ApplyAsync(context, conversation.Id, second.Id, new CommandRequest { Action = "stop", IdempotencyKey = "fixture-cleanup-second" }, "fixture", token).ConfigureAwait(false);

                }
            }
            finally
            {
                if (conversation != null) await database.DeleteConversationAsync(context, conversation.Id, CancellationToken.None).ConfigureAwait(false);
                if (profile != null) await database.Profiles.DeleteAsync(context, profile.Id, CancellationToken.None).ConfigureAwait(false);
                UserRecord current = await database.Users.ReadAsync(owner, user.Id, CancellationToken.None).ConfigureAwait(false);
                if (current.Active) await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = current.Name, Email = current.Email, Active = false, ExpectedRevision = current.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task PausedAsync(DatabaseDriverBase database, RequestContext context, string conversation, string run, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                while ((await database.Runs.ReadAsync(context, conversation, run, deadline.Token).ConfigureAwait(false)).State != "paused") await Task.Delay(20, deadline.Token).ConfigureAwait(false);
            }
        }
    }
}
