namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using TUIKit.Hosting;

    /// <summary>Answers questions agents ask through a sequence of modal dialogs, and lists the questions waiting across conversations. Nothing is answered inline in the transcript.</summary>
    internal sealed class QuestionWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;

        internal QuestionWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }

        private Task<T> Ui<T>(Func<Task<T>> action) { return _Dispatcher.InvokeAsync(action); }
        private Task<int> SelectAsync(string title, IEnumerable<string> choices) { return Ui(() => _Application.SelectStyledAsync(title, choices.ToArray())); }
        private Task<string?> PromptAsync(string title, string initial = "") { return Ui(() => _Application.PromptStyledAsync(title, initial)); }

        /// <summary>The Questions screen: open questions in your conversations, or the whole tenant, each opening the question dialogs.</summary>
        internal async Task RunAsync()
        {
            string scope = "mine";
            while (!_Token.IsCancellationRequested)
            {
                QuestionPage page = await _Client.QuestionsAsync(new QuestionFilter { Status = "open", Scope = scope, PageSize = 50 }, _Token).ConfigureAwait(false);
                List<string> labels = page.Items.Select(Label).ToList();
                labels.Add(scope == "mine" ? "Show questions in every conversation in this tenant" : "Show only my conversations");
                labels.Add("Back");
                string title = page.TotalCount == 0 ? "No questions are waiting for you" : page.TotalCount + (page.TotalCount == 1 ? " question is" : " questions are") + " waiting" + (scope == "mine" ? " in your conversations" : " in this tenant");
                int choice = await SelectAsync(title, labels).ConfigureAwait(false);
                if (choice < 0 || choice == labels.Count - 1) return;
                if (choice == labels.Count - 2) { scope = scope == "mine" ? "tenant" : "mine"; continue; }
                await AnswerAsync(page.Items[choice]).ConfigureAwait(false);
            }
        }

        /// <summary>Answer the open questions of one run, one request at a time.</summary>
        internal async Task RunForRunAsync(string conversationId, string runId)
        {
            List<QuestionRequest> open = (await _Client.RunQuestionsAsync(conversationId, runId, _Token).ConfigureAwait(false)).Where(item => item.Status == "open").ToList();
            if (open.Count == 0) { await SelectAsync("No questions from this debate are waiting", new[] { "Back" }).ConfigureAwait(false); return; }
            for (int index = 0; index < open.Count; index++)
                if (!await AnswerAsync(open[index], index + 1, open.Count).ConfigureAwait(false)) return;
        }

        /// <summary>Walk the user through one request. Returns false when the user closed the dialogs without resolving it.</summary>
        internal async Task<bool> AnswerAsync(QuestionRequest request, int position = 1, int count = 1)
        {
            request = await _Client.QuestionAsync(request.Id, _Token).ConfigureAwait(false);
            if (request.Status != "open") { await SelectAsync(Resolved(request), new[] { "Close" }).ConfigureAwait(false); return true; }
            string counter = count > 1 ? " (" + position + " of " + count + ")" : "";
            StringBuilder intro = new StringBuilder();
            intro.Append(Asker(request)).Append(" asks").Append(request.ConversationTitle.Length == 0 ? "" : " in \"" + request.ConversationTitle + "\"").Append(counter).Append('.');
            if (request.Context.Length > 0) intro.Append("\n\n").Append(request.Context);
            intro.Append("\n\n").Append(string.Join("\n", request.Questions.Select(question => "- " + question.Header + ": " + question.Prompt)));
            intro.Append(request.CredentialWarning ? "\n\nWarning: this looks like a request for a password, token or key. ArgueBot never needs secrets in answers." : "\n\nDo not paste passwords, tokens or keys into answers.");
            int start = await SelectAsync(intro.ToString(), new[] { "Answer now", "Answer later (the waiting turn ends; the debate resumes after you answer)", "Skip (the agent continues on assumptions)", "Close" }).ConfigureAwait(false);
            if (start == 1) { await Guard(() => _Client.DeferQuestionAsync(request.Id, _Token)).ConfigureAwait(false); return false; }
            if (start == 2) return await SkipAsync(request).ConfigureAwait(false);
            if (start != 0) return false;
            QuestionAnswerRequest answers = new QuestionAnswerRequest { IdempotencyKey = Guid.NewGuid().ToString("N") };
            for (int index = 0; index < request.Questions.Count; index++)
            {
                QuestionAnswerInput? input = await AskAsync(request.Questions[index], index + 1, request.Questions.Count).ConfigureAwait(false);
                if (input == null) return false;
                answers.Answers.Add(input);
            }
            string summary = string.Join("\n", request.Questions.Select((question, index) => "- " + question.Header + ": " + Describe(question, answers.Answers[index])));
            int submit = await SelectAsync("Send these answers?\n\n" + summary, new[] { "Submit", "Start over", "Close without answering" }).ConfigureAwait(false);
            if (submit == 1) return await AnswerAsync(request, position, count).ConfigureAwait(false);
            if (submit != 0) return false;
            return await Guard(() => _Client.AnswerQuestionAsync(request.Id, answers, _Token)).ConfigureAwait(false);
        }

        private async Task<QuestionAnswerInput?> AskAsync(Question question, int number, int total)
        {
            string title = question.Header + (total > 1 ? " (" + number + " of " + total + ")" : "") + "\n\n" + question.Prompt + (question.Required ? "" : "\n\nOptional.");
            QuestionAnswerInput input = new QuestionAnswerInput { QuestionId = question.Id };
            switch (question.Kind)
            {
                case "confirm":
                    {
                        List<string> choices = new List<string> { "Yes", "No" };
                        if (!question.Required) choices.Add("Leave unanswered");
                        int choice = await SelectAsync(title, choices).ConfigureAwait(false);
                        if (choice < 0) return null;
                        if (choice == 2) input.Skip = true; else input.Confirmed = choice == 0;
                        return input;
                    }
                case "text":
                    {
                        string? text = await PromptAsync(title + "\n\nUp to " + question.MaxLength + " characters.", "").ConfigureAwait(false);
                        if (text == null) return null;
                        text = text.Trim();
                        if (text.Length == 0 && question.Required) return await AskAsync(question, number, total).ConfigureAwait(false);
                        if (text.Length == 0) input.Skip = true; else input.Text = text;
                        return input;
                    }
                case "single_choice":
                    {
                        List<string> choices = question.Options.Select(OptionLabel).ToList();
                        if (question.AllowOther) choices.Add("Other...");
                        if (!question.Required) choices.Add("Leave unanswered");
                        int choice = await SelectAsync(title, choices).ConfigureAwait(false);
                        if (choice < 0) return null;
                        if (choice < question.Options.Count) { input.Selected.Add(question.Options[choice].Key); return input; }
                        if (question.AllowOther && choice == question.Options.Count) return await OtherAsync(question, input, number, total).ConfigureAwait(false);
                        input.Skip = true; return input;
                    }
                default:
                    {
                        List<string> choices = question.Options.Select(OptionLabel).ToList();
                        if (question.AllowOther) choices.Add("Other...");
                        int minimum = question.Required && !question.AllowOther ? Math.Max(1, question.MinSelections) : question.MinSelections;
                        string description = question.Prompt + "\n\nChoose " + (question.MinSelections == question.MaxSelections ? question.MaxSelections.ToString() : Math.Max(minimum, 0) + " to " + question.MaxSelections) + ".";
                        int[]? picked = await Ui(async () =>
                        {
                            ChecklistDialog dialog = new ChecklistDialog(question.Header, description, choices.ToArray(), minimum, question.MaxSelections + (question.AllowOther ? 1 : 0));
                            dialog.ApplyTheme(_Application.Theme);
                            object? result = await _Application.ShowAsync(dialog).ConfigureAwait(false);
                            return result as int[];
                        }).ConfigureAwait(false);
                        if (picked == null) return null;
                        foreach (int index in picked.Where(index => index < question.Options.Count)) input.Selected.Add(question.Options[index].Key);
                        if (question.AllowOther && picked.Contains(question.Options.Count)) return await OtherAsync(question, input, number, total).ConfigureAwait(false);
                        if (picked.Length == 0) input.Skip = !question.Required;
                        return input;
                    }
            }
        }

        private async Task<QuestionAnswerInput?> OtherAsync(Question question, QuestionAnswerInput input, int number, int total)
        {
            string? other = await PromptAsync(question.Header + ": Other\n\n" + question.Prompt, "").ConfigureAwait(false);
            if (other == null) return null;
            if (other.Trim().Length == 0) return await AskAsync(question, number, total).ConfigureAwait(false);
            input.OtherText = other.Trim();
            return input;
        }

        private async Task<bool> SkipAsync(QuestionRequest request)
        {
            string? note = await PromptAsync("Skip: optionally tell the agent why or what to assume (Enter to leave empty)", "").ConfigureAwait(false);
            if (note == null) return false;
            return await Guard(() => _Client.SkipQuestionAsync(request.Id, new QuestionSkipRequest { Note = note.Trim(), IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token)).ConfigureAwait(false);
        }

        /// <summary>Send a resolution and explain a conflict when someone else resolved the request first.</summary>
        private async Task<bool> Guard(Func<Task<QuestionRequest>> send)
        {
            try { await send().ConfigureAwait(false); return true; }
            catch (DomainException error) when (error.Code == "question_already_resolved")
            {
                await SelectAsync(error.Message + " Your answer was not sent.", new[] { "Close" }).ConfigureAwait(false);
                return true;
            }
        }

        private static string Describe(Question question, QuestionAnswerInput input)
        {
            if (input.Skip) return "not answered";
            List<string> parts = question.Options.Where(option => input.Selected.Contains(option.Key)).Select(option => option.Label).ToList();
            if (input.OtherText.Length > 0) parts.Add("Other: " + input.OtherText);
            if (input.Text.Length > 0) parts.Add(input.Text);
            if (input.Confirmed.HasValue) parts.Add(input.Confirmed.Value ? "Yes" : "No");
            return string.Join("; ", parts);
        }

        private static string OptionLabel(QuestionOption option) { return option.Label + (option.Recommended ? " (recommended)" : "") + (option.Description.Length == 0 ? "" : " - " + option.Description); }

        private static string Asker(QuestionRequest request) { return request.AskerName.Length == 0 ? request.AskerId : request.AskerName; }

        private static string Label(QuestionRequest request)
        {
            string headers = string.Join(", ", request.Questions.Select(question => question.Header));
            return Asker(request) + ": " + headers + (request.ConversationTitle.Length == 0 ? "" : " | " + request.ConversationTitle);
        }

        private static string Resolved(QuestionRequest request)
        {
            return request.Status switch
            {
                "answered" => "This question was already answered.",
                "skipped" => "This question was skipped.",
                "expired" => "This question expired before it was answered.",
                _ => "This question was cancelled because its debate stopped."
            };
        }
    }
}
