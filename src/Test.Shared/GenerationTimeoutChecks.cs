namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class GenerationTimeoutChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-timeout-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    RunnerProfile profile = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Deadline fixture", Provider = "claude", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "diagnostic-timeout" }, TimeoutSeconds = 1, WorkingDirectory = home }, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Deadline fixture", token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(database, new CliRunner(), diagnosticDirectory: Path.Combine(home, "logs", "runner-failures")))
                    {
                        DebateRun run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional deadline test", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "timeout" }, token).ConfigureAwait(false);
                        run = await WaitAsync(database, owner, run, "paused", token).ConfigureAwait(false);
                        GenerationAttempt attempt = (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single();
                        await engine.DescribeFailureAsync(attempt, token).ConfigureAwait(false);
                        Message answer = await database.ReadMessageAsync(owner, conversation.Id, attempt.MessageId, token).ConfigureAwait(false);
                        if (run.Outcome != "generation_timeout" || attempt.State != "failed" || answer.State != "failed" || attempt.ErrorCode != "generation_timeout" || !attempt.ErrorMessage.Contains("Resume keeps the original timeout", StringComparison.Ordinal)) throw new InvalidDataException("Deadline was hidden as an unexplained interruption.");
                        // An explicit interrupt must retain cancellation semantics instead of a deadline failure.
                        profile.TimeoutSeconds = 30;
                        await database.Profiles.UpdateAsync(owner, profile.Id, profile, token).ConfigureAwait(false);
                        conversation = await database.CreateConversationAsync(owner, "Interrupt fixture", token).ConfigureAwait(false);
                        run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional interrupt test", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "interrupt" }, token).ConfigureAwait(false);
                        run = await WaitAsync(database, owner, run, "agent_turn", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "interrupt", IdempotencyKey = "interrupt-command" }, "fixture", token).ConfigureAwait(false);
                        run = await WaitAsync(database, owner, run, "paused", token).ConfigureAwait(false);
                        attempt = (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single();
                        await engine.DescribeFailureAsync(attempt, token).ConfigureAwait(false);
                        if (attempt.State != "interrupted" || attempt.ErrorCode != "generation_interrupted" || !attempt.ErrorMessage.Contains("explicitly interrupted", StringComparison.Ordinal)) throw new InvalidDataException("Explicit interruption was misclassified as timeout or lost its recovery guidance.");
                        conversation = await database.CreateConversationAsync(owner, "Stop fixture", token).ConfigureAwait(false);
                        run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional stop test", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "stop" }, token).ConfigureAwait(false);
                        run = await WaitAsync(database, owner, run, "agent_turn", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = "stop-command" }, "fixture", token).ConfigureAwait(false);
                        run = await WaitAsync(database, owner, run, "cancelled", token).ConfigureAwait(false);
                        attempt = (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single();
                        if (attempt.State != "interrupted" || attempt.ErrorCode != "generation_stopped") throw new InvalidDataException("Explicit stop lost its interrupted checkpoint cause.");
                    }
                    conversation = await database.CreateConversationAsync(owner, "Shutdown fixture", token).ConfigureAwait(false);
                    DebateRun shutdownRun;
                    await using (ConversationEngine shutdown = new ConversationEngine(database, new CliRunner()))
                    {
                        shutdownRun = await shutdown.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional shutdown test", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "shutdown" }, token).ConfigureAwait(false);
                        shutdownRun = await WaitAsync(database, owner, shutdownRun, "agent_turn", token).ConfigureAwait(false);
                    }
                    GenerationAttempt shutdownAttempt = (await database.Attempts.EnumerateAsync(owner, conversation.Id, shutdownRun.Id, token: token).ConfigureAwait(false)).Items.Single();
                    if (shutdownAttempt.State != "interrupted" || shutdownAttempt.ErrorCode != "service_shutdown") throw new InvalidDataException("Orderly service shutdown lost its interrupted checkpoint cause.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task<DebateRun> WaitAsync(SqliteDatabaseDriver database, RequestContext owner, DebateRun run, string state, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                do
                {
                    await Task.Delay(20, deadline.Token).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(owner, run.ConversationId, run.Id, deadline.Token).ConfigureAwait(false);
                } while (run.State != state);
                return run;
            }
        }
    }
}
