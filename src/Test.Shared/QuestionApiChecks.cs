namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Question REST routes against a running service: authentication, validation, the inbox, the change feed, answering with conflicts, and tenant isolation.</summary>
    internal static class QuestionApiChecks
    {
        internal static async Task RunAsync(string endpoint, string home, CancellationToken token)
        {
            using (HttpClient http = new HttpClient(LocalServiceConnector.CreateHttpHandler(home), true) { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") })
            using (ArgueBotClient admin = LocalServiceConnector.CreateClient(endpoint, home, IdentitySeed.Token))
            using (ArgueBotClient outsider = LocalServiceConnector.CreateClient(endpoint, home, ""))
            {
                foreach (string path in new[] { "v2.0/api/questions", "v2.0/api/questions/summary", "v2.0/api/questions/events", "v2.0/api/questions/qrq_missing" })
                    await Status(http, HttpMethod.Get, path, 401, token).ConfigureAwait(false);
                foreach (string path in new[] { "v2.0/api/questions/qrq_missing/answer", "v2.0/api/questions/qrq_missing/skip", "v2.0/api/questions/qrq_missing/defer" })
                    await Status(http, HttpMethod.Post, path, 401, token).ConfigureAwait(false);
                await Expect(() => admin.QuestionAsync("qrq_missing", token), 404, "not_found").ConfigureAwait(false);
                await Expect(() => admin.AnswerQuestionAsync("qrq_missing", new QuestionAnswerRequest(), token), 404, "not_found").ConfigureAwait(false);
                await Expect(() => admin.SkipQuestionAsync("qrq_missing", new QuestionSkipRequest(), token), 404, "not_found").ConfigureAwait(false);
                await Expect(() => admin.DeferQuestionAsync("qrq_missing", token), 404, "not_found").ConfigureAwait(false);
                await Expect(() => admin.QuestionsAsync(new QuestionFilter { Scope = "everyone" }, token), 400, "invalid_filter").ConfigureAwait(false);
                await Expect(() => admin.QuestionsAsync(new QuestionFilter { Status = "pending" }, token), 400, "invalid_filter").ConfigureAwait(false);
                await Expect(() => admin.QuestionsAsync(new QuestionFilter { PageSize = 101 }, token), 400, "invalid_page").ConfigureAwait(false);
                await Expect(() => admin.QuestionEventsAsync("not-a-cursor", 10, token), 400, "invalid_cursor").ConfigureAwait(false);
                QuestionEventPage cursor = await admin.QuestionEventsAsync("", 10, token).ConfigureAwait(false);
                Require(cursor.Items.Count == 0 && cursor.Next.Length > 0, "An empty cursor must return the newest cursor and no changes.");
                QuestionSummary empty = await admin.QuestionSummaryAsync(token).ConfigureAwait(false);
                Require(empty.OpenMine == 0 && empty.OpenTenant == 0, "A new workspace has no open questions.");
                await Expect(() => admin.SendAsync<object>(HttpMethod.Get, "v2.0/api/conversations/cnv_missing/runs/run_missing/clarifications", token: token), 404, "not_found").ConfigureAwait(false);

                string? simulation = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL");
                if (string.IsNullOrWhiteSpace(simulation)) return;

                // A REST agent asks; the question is answered over HTTP while the call is held, and the debate finishes.
                EndpointProfile simulator = await admin.SaveEndpointAsync(new EndpointProfile { Name = "Question API simulation", Protocol = "ollama", BaseUrl = simulation, TimeoutSeconds = 20 }, token).ConfigureAwait(false);
                ModelProfile asking = await admin.SaveModelAsync(new ModelProfile { Name = "Question API asker", EndpointId = simulator.Id, Model = "multi-agent-questions", SupportsTools = true }, token).ConfigureAwait(false);
                ModelProfile panel = await admin.SaveModelAsync(new ModelProfile { Name = "Question API panel", EndpointId = simulator.Id, Model = "multi-agent", SupportsTools = true }, token).ConfigureAwait(false);
                RunnerProfile orchestrator = await admin.CreateParticipantAsync(new RunnerProfile { Name = "Question API orchestrator", ModelProfileId = panel.Id }, token).ConfigureAwait(false);
                RunnerProfile asker = await admin.CreateParticipantAsync(new RunnerProfile { Name = "Question API asker", ModelProfileId = asking.Id }, token).ConfigureAwait(false);
                RunnerProfile panelist = await admin.CreateParticipantAsync(new RunnerProfile { Name = "Question API panelist", ModelProfileId = panel.Id }, token).ConfigureAwait(false);
                Conversation conversation = await admin.CreateConversationAsync("Question API journey", token).ConfigureAwait(false);
                DebateRun run = await admin.StartAsync(conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = orchestrator.Id, ParticipantIds = new List<string> { asker.Id, panelist.Id }, Question = "How should we roll out?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off", QuestionHoldSeconds = 60 }, IdempotencyKey = "question-api" }, token).ConfigureAwait(false);
                QuestionRequest? open = null;
                for (int attempt = 0; attempt < 200 && open == null; attempt++)
                {
                    open = (await admin.QuestionsAsync(new QuestionFilter { Status = "open", ConversationId = conversation.Id }, token).ConfigureAwait(false)).Items.FirstOrDefault(item => item.HoldState == "held");
                    if (open == null) await Task.Delay(50, token).ConfigureAwait(false);
                }
                Require(open != null, "The REST agent did not ask its question.");
                Require((await admin.RunAsync(conversation.Id, run.Id, token).ConfigureAwait(false)).State == "awaiting_input", "The run must report that it is waiting for input.");
                Require((await admin.QuestionSummaryAsync(token).ConfigureAwait(false)).OpenMine == 1, "The summary must count the open question.");
                QuestionEventPage changes = await admin.QuestionEventsAsync(cursor.Next, 100, token).ConfigureAwait(false);
                Require(changes.Items.Any(item => item.RequestId == open!.Id && item.Status == "opened"), "The change feed must report the new question.");
                Require((await admin.RunQuestionsAsync(conversation.Id, run.Id, token).ConfigureAwait(false)).Single().Id == open!.Id, "The run route must list the run's question.");

                Tenant tenant = await admin.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = "Question fixture tenant" }, token).ConfigureAwait(false);
                admin.TenantId = tenant.Id;
                User outsiderUser = await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { FirstName = "Question", LastName = "Outsider", Email = "questions@example.test", Password = "outsider-password" }, token).ConfigureAwait(false);
                AuthCredentialCreated credential = await admin.SendAsync<AuthCredentialCreated>(HttpMethod.Post, "v2.0/api/users/" + outsiderUser.Id + "/credentials", new AuthCredentialRequest { Name = "Question fixture" }, token).ConfigureAwait(false);
                admin.TenantId = "";
                outsider.Token = credential.Token;
                await Expect(() => outsider.QuestionAsync(open.Id, token), 404, "not_found").ConfigureAwait(false);
                await Expect(() => outsider.AnswerQuestionAsync(open.Id, Choose("prod", "outsider"), token), 404, "not_found").ConfigureAwait(false);
                Require((await outsider.QuestionsAsync(new QuestionFilter { Scope = "tenant" }, token).ConfigureAwait(false)).TotalCount == 0 && (await outsider.QuestionSummaryAsync(token).ConfigureAwait(false)).OpenTenant == 0, "Another tenant must see no questions.");

                await Expect(() => admin.AnswerQuestionAsync(open.Id, Choose("missing", "bad"), token), 400, "invalid_answer").ConfigureAwait(false);
                QuestionRequest answered = await admin.AnswerQuestionAsync(open.Id, Choose("prod", "first"), token).ConfigureAwait(false);
                Require(answered.Status == "answered" && answered.Questions[0].Answer!.SelectedLabels.Single() == "Production only", "POST answer must store the choice.");
                Require((await admin.AnswerQuestionAsync(open.Id, Choose("prod", "first"), token).ConfigureAwait(false)).Status == "answered", "Repeating an answer with the same key must succeed.");
                await Expect(() => admin.AnswerQuestionAsync(open.Id, Choose("all", "second"), token), 409, "question_already_resolved").ConfigureAwait(false);
                await Expect(() => admin.SkipQuestionAsync(open.Id, new QuestionSkipRequest { IdempotencyKey = "late" }, token), 409, "question_already_resolved").ConfigureAwait(false);
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    run = await admin.RunAsync(conversation.Id, run.Id, token).ConfigureAwait(false);
                    if (run.State == "completed" || run.State == "paused" || run.State == "failed") break;
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                Require(run.State == "completed", "The debate must finish after the answer: " + run.State + "/" + run.Outcome);
                Require((await admin.QuestionsAsync(new QuestionFilter { Status = "resolved", ConversationId = conversation.Id }, token).ConfigureAwait(false)).TotalCount == 1, "The resolved filter must list the answered question.");
            }
        }

        private static QuestionAnswerRequest Choose(string option, string key)
        { return new QuestionAnswerRequest { Answers = new List<QuestionAnswerInput> { new QuestionAnswerInput { Key = "scope", Selected = new List<string> { option } } }, IdempotencyKey = key }; }

        private static async Task Status(HttpClient http, HttpMethod method, string path, int status, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                if ((int)response.StatusCode != status) throw new InvalidDataException(method + " " + path + " returned " + (int)response.StatusCode + " instead of " + status + ".");
        }

        private static async Task Expect(Func<Task> action, int status, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.StatusCode == status && ex.Code == code) { return; }
            catch (DomainException ex) { throw new InvalidDataException("Expected " + status + " " + code + " but got " + ex.StatusCode + " " + ex.Code + ": " + ex.Message); }
            throw new InvalidDataException("Expected " + status + " " + code + " but the request succeeded.");
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
