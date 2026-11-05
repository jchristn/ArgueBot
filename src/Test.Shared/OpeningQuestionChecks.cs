namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Conversation titles stay separate from the opening question that debates default to.</summary>
    internal static class OpeningQuestionChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-opening-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    List<string> seats = new List<string>();
                    for (int index = 0; index < 2; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Opening seat " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);

                    Conversation withQuestion = await database.CreateConversationAsync(context, "Billing isolation", token, "  Should billing move to its own service?  ").ConfigureAwait(false);
                    Require(withQuestion.Title == "Billing isolation" && withQuestion.OpeningQuestion == "Should billing move to its own service?", "Create must keep the title and trimmed opening question separately.");
                    Conversation renamed = await database.UpdateConversationAsync(context, withQuestion.Id, new ConversationRequest { Title = "Billing split", Pinned = true, Revision = withQuestion.Revision }, token).ConfigureAwait(false);
                    Require(renamed.Title == "Billing split" && renamed.OpeningQuestion == withQuestion.OpeningQuestion, "An update that omits the opening question must keep it.");
                    DebateRun run = await database.Runs.CreateAsync(context, withQuestion.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats, IdempotencyKey = "opening-default" }, token).ConfigureAwait(false);
                    Require(run.Question == "Should billing move to its own service?", "A debate without an explicit question must start from the opening question, not the title: " + run.Question);
                    renamed = await database.ReadConversationAsync(context, withQuestion.Id, token).ConfigureAwait(false);
                    Conversation edited = await database.UpdateConversationAsync(context, withQuestion.Id, new ConversationRequest { Title = renamed.Title, OpeningQuestion = "A different question", Revision = renamed.Revision }, token).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, withQuestion.Id, run.Id, token).ConfigureAwait(false);
                    Require(edited.OpeningQuestion == "A different question" && run.Question == "Should billing move to its own service?", "Editing the opening question must not change a started debate.");

                    Conversation titleOnly = await database.CreateConversationAsync(context, "Which design should we choose?", token).ConfigureAwait(false);
                    DebateRun fallback = await database.Runs.CreateAsync(context, titleOnly.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats, IdempotencyKey = "opening-fallback" }, token).ConfigureAwait(false);
                    Require(fallback.Question == "Which design should we choose?", "A blank opening question must fall back to the title.");
                    Conversation explicitConversation = await database.CreateConversationAsync(context, "Explicit", token, "Opening question that the request overrides").ConfigureAwait(false);
                    DebateRun explicitRun = await database.Runs.CreateAsync(context, explicitConversation.Id, new CreateRunRequest { ProtocolVersion = 2, Question = "Explicit question", OrchestratorId = seats[0], ParticipantIds = seats, IdempotencyKey = "opening-explicit" }, token).ConfigureAwait(false);
                    Require(explicitRun.Question == "Explicit question", "An explicit debate question must win over the conversation defaults.");

                    try { await database.CreateConversationAsync(context, "Too long", token, new string('q', 4001)).ConfigureAwait(false); throw new InvalidDataException("An overlong opening question was accepted."); }
                    catch (DomainException failure) { Require(failure.Code == "invalid_question" && failure.Field == "openingQuestion", "An overlong opening question must name its field."); }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
