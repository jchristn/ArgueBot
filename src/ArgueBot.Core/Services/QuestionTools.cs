namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;

    /// <summary>The ask_user conversation tool, bound to one invocation. Tool input never chooses the tenant, conversation, run or asking seat.</summary>
    public sealed class QuestionTools
    {
        /// <summary>Tool name.</summary>
        public const string AskUser = "ask_user";

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private const string Schema = """{"type":"object","properties":{"context":{"type":"string","maxLength":2000},"questions":{"type":"array","minItems":1,"maxItems":4,"items":{"type":"object","properties":{"key":{"type":"string"},"header":{"type":"string","maxLength":40},"prompt":{"type":"string","maxLength":1000},"kind":{"type":"string","enum":["text","single_choice","multi_choice","confirm"]},"options":{"type":"array","maxItems":12,"items":{"type":"object","properties":{"id":{"type":"string"},"label":{"type":"string","maxLength":80},"description":{"type":"string"},"recommended":{"type":"boolean"}},"required":["id","label"]}},"allowOther":{"type":"boolean"},"minSelections":{"type":"integer"},"maxSelections":{"type":"integer"},"maxLength":{"type":"integer"},"placeholder":{"type":"string"},"required":{"type":"boolean"}},"required":["prompt","kind"]}},"wait":{"type":"boolean"},"visibility":{"type":"string","enum":["panel","self"]}},"required":["questions"]}""";

        private readonly DatabaseDriverBase _Database;
        private readonly RequestContext _Context;
        private readonly DebateRun _Run;
        private readonly string _AskerId;
        private readonly string _AskerName;
        private readonly string _Role;
        private readonly string _AttemptId;
        private readonly Func<QuestionRequest, CancellationToken, Task<string>>? _Wait;

        /// <summary>Bind the tool to one invocation.</summary>
        /// <param name="database">Service database.</param>
        /// <param name="context">Run owner's context.</param>
        /// <param name="run">Run being executed.</param>
        /// <param name="askerId">Speaking seat or orchestrator profile.</param>
        /// <param name="askerName">Display name of the speaker.</param>
        /// <param name="attemptId">Current attempt.</param>
        /// <param name="wait">Service callback that holds the call for the answer and returns <see cref="QuestionCoordinator.Resolved"/> or <see cref="QuestionCoordinator.Parked"/>; null parks every waiting request at once.</param>
        public QuestionTools(DatabaseDriverBase database, RequestContext context, DebateRun run, string askerId, string askerName, string attemptId, Func<QuestionRequest, CancellationToken, Task<string>>? wait)
        {
            ArgumentNullException.ThrowIfNull(database); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(run);
            _Database = database; _Context = context; _Run = run; _AskerId = askerId; _AskerName = askerName; _AttemptId = attemptId; _Wait = wait;
            _Role = Role(run);
        }

        /// <summary>Whether the call names this tool.</summary>
        /// <param name="name">Tool name.</param>
        /// <returns>True for ask_user.</returns>
        public static bool IsQuestionTool(string name) { return name == AskUser; }

        /// <summary>The asking role for the run's current step: followup, orchestrator (intake, question and synthesis steps) or participant.</summary>
        /// <param name="run">Run being executed.</param>
        /// <returns>The role.</returns>
        public static string Role(DebateRun run)
        {
            ArgumentNullException.ThrowIfNull(run);
            if (run.Mode == "followup") return "followup";
            return run.Phase == "intake" || run.Phase == "question" || run.Phase == "synthesis" ? "orchestrator" : "participant";
        }

        /// <summary>The ask_user definition when the debate policy lets this step ask; empty otherwise. Summaries never ask.</summary>
        /// <param name="run">Run being executed.</param>
        /// <returns>Zero or one definitions.</returns>
        public static List<ConversationToolDefinition> Definitions(DebateRun run)
        {
            ArgumentNullException.ThrowIfNull(run);
            List<ConversationToolDefinition> result = new List<ConversationToolDefinition>();
            if (run.Phase == "compaction" || run.Policy.UserQuestions == "off" || run.Policy.MaxQuestionRequestsPerTurn == 0 || run.Policy.MaxQuestionRequestsPerRun == 0) return result;
            string role = Role(run);
            if (run.Policy.UserQuestions == "orchestrator" && role == "participant") return result;
            result.Add(new ConversationToolDefinition { Name = AskUser, Description = "Ask the person running the debate 1 to 4 questions when their answer would change your work and is not in the conversation. Prefer choices with a recommended option. Never ask for secrets. Returns the answer; if status is parked, end your turn in one sentence; if skipped, state your assumptions.", Schema = Schema });
            return result;
        }

        /// <summary>Validate and execute one ask_user call, returning a structured tool result.</summary>
        /// <param name="call">The call.</param>
        /// <param name="token">Cancelled when the turn is interrupted or stopped.</param>
        /// <returns>JSON with success and result, or success false with code and error.</returns>
        /// <exception cref="OperationCanceledException">The turn was cancelled while waiting.</exception>
        public async Task<string> ExecuteAsync(ConversationToolCall call, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(call);
            try
            {
                if (Definitions(_Run).Count == 0) throw new DomainException(403, "user_questions_disabled", "This debate does not let this step ask the user. Continue with what you know.");
                if (call.ArgumentsJson.Length > 32768) throw new DomainException(400, "invalid_question", "Tool arguments exceed 32 KiB.", "questions");
                AskUserArguments args = JsonSerializer.Deserialize<AskUserArguments>(call.ArgumentsJson, _Json) ?? throw new JsonException();
                await _Database.Budgets.ReserveToolAsync(_Context, _Run.Id, _AttemptId, call.CallId, token).ConfigureAwait(false);
                int maximum = _Run.Phase == "intake" ? Math.Min(_Run.Policy.MaxIntakeQuestions, QuestionRules.MaxQuestions) : QuestionRules.MaxQuestions;
                if (args.Questions.Count > maximum) throw new DomainException(400, _Run.Phase == "intake" ? "intake_question_limit" : "invalid_question", "Ask at most " + maximum + " questions in one request.", "questions");
                QuestionAsk ask = new QuestionAsk
                {
                    ConversationId = _Run.ConversationId, RunId = _Run.Id, AttemptId = _AttemptId, CallId = _AttemptId + ":" + call.CallId,
                    AskerId = _AskerId, AskerName = _AskerName, AskerRole = _Role, Phase = _Run.Phase, Round = _Run.Round,
                    Context = args.Context, Wait = args.Wait ?? true, Visibility = args.Visibility.Length == 0 ? "panel" : args.Visibility,
                    ExpiresUtc = _Run.Policy.QuestionExpirySeconds > 0 ? DateTime.UtcNow.AddSeconds(_Run.Policy.QuestionExpirySeconds) : null,
                    MaxPerTurn = _Run.Policy.MaxQuestionRequestsPerTurn, MaxPerRun = _Run.Policy.MaxQuestionRequestsPerRun,
                    Questions = args.Questions.Select(item => new Question
                    {
                        Key = item.Key, Header = item.Header, Prompt = item.Prompt, Kind = item.Kind, AllowOther = item.AllowOther, MinSelections = item.MinSelections, MaxSelections = item.MaxSelections, MaxLength = item.MaxLength, Placeholder = item.Placeholder, Required = item.Required ?? true,
                        Options = item.Options.Select(option => new QuestionOption { Key = option.Id, Label = option.Label, Description = option.Description, Recommended = option.Recommended }).ToList()
                    }).ToList()
                };
                QuestionAskResult stored = await _Database.Questions.CreateAsync(_Context, ask, token).ConfigureAwait(false);
                QuestionRequest request = stored.Request;
                if (request.Status != "open") return Serialize(Result(request));
                if (!request.Wait) return Serialize(new { status = "recorded", requestId = request.Id, message = "Recorded. Continue your turn; the answer reaches your next turn as moderator context." });
                string outcome = _Wait == null ? QuestionCoordinator.Parked : await _Wait(request, token).ConfigureAwait(false);
                request = await _Database.Questions.ReadAsync(_Context, request.Id, CancellationToken.None).ConfigureAwait(false);
                if (outcome == QuestionCoordinator.Parked && request.Status == "open") return Serialize(new { status = "parked", requestId = request.Id, message = "The user has not answered yet. End your turn now in one short sentence saying what you are waiting for. Do not draw conclusions that depend on the answer. You will continue this step once they answer." });
                return Serialize(Result(request));
            }
            catch (DomainException exception) when (exception.Code != "tool_budget_exhausted")
            { return JsonSerializer.Serialize(new { success = false, code = exception.Code, error = exception.Message, field = exception.Field }, _Json); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is NullReferenceException)
            { return "{\"success\":false,\"code\":\"invalid_question\",\"error\":\"Use the documented ask_user schema.\"}"; }
        }

        /// <summary>The tool result for a resolved request, as the asking agent receives it.</summary>
        /// <param name="request">The request.</param>
        /// <returns>An anonymous result object.</returns>
        public static object Result(QuestionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Status == "answered")
                return new { status = "answered", requestId = request.Id, answeredUtc = request.ResolvedUtc, answers = request.Questions.Select(question => new { key = question.Key, kind = question.Kind, skipped = question.Answer?.Skipped ?? true, selected = question.Answer?.Selected ?? new List<string>(), selectedLabels = question.Answer?.SelectedLabels ?? new List<string>(), otherText = question.Answer?.OtherText ?? "", text = question.Answer?.Text ?? "", confirmed = question.Answer?.Confirmed }) };
            if (request.Status == "skipped" || request.Status == "expired")
                return new { status = "skipped", requestId = request.Id, reason = request.Status == "expired" ? "expired" : "skipped_by_user", note = request.ResolutionNote, message = "No answer is available. Continue on stated assumptions and say what you assumed." };
            if (request.Status == "cancelled") return new { status = "cancelled", requestId = request.Id, message = "The debate was stopped. End your turn." };
            return new { status = "parked", requestId = request.Id, message = "The user has not answered yet. End your turn now in one short sentence." };
        }

        private static string Serialize(object result) { return JsonSerializer.Serialize(new { success = true, result }, _Json); }
    }
}
