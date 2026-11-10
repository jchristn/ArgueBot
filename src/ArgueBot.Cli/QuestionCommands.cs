namespace ArgueBot.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;

    /// <summary>The questions command: list, show, answer, skip, defer, watch and wait. Agent-written text is printed as data.</summary>
    internal static class QuestionCommands
    {
        /// <summary>Exit code when a run is waiting for an answer and no answer can be given here (no terminal, or --no-input).</summary>
        internal const int WaitingForInput = 4;

        internal const string Usage = "Use questions list [--status S] [--conversation ID] [--run ID] [--all] [--page N], show ID, answer ID [--choice KEY=OPTION[,OPTION]] [--other KEY=TEXT] [--text KEY=TEXT] [--confirm KEY=yes|no] [--skip-question KEY] [--file PATH|-], skip ID [--note TEXT], defer ID, delete ID [ID...], watch, or wait CONVERSATION RUN [--no-input].";

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static async Task<int> RunAsync(ArgueBotClient client, List<string> words, CancellationToken token)
        {
            string operation = words.Count > 1 ? words[1] : "list";
            List<string> rest = words.Skip(2).ToList();
            switch (operation)
            {
                case "list":
                    {
                        QuestionFilter filter = new QuestionFilter { Status = Option(rest, "--status") ?? "", ConversationId = Option(rest, "--conversation") ?? "", RunId = Option(rest, "--run") ?? "", Scope = rest.Remove("--all") ? "tenant" : "mine", PageNumber = int.Parse(Option(rest, "--page") ?? "1", CultureInfo.InvariantCulture) };
                        Program.Print(await client.QuestionsAsync(filter, token).ConfigureAwait(false));
                        return 0;
                    }
                case "show":
                    Program.Print(await client.QuestionAsync(Target(rest), token).ConfigureAwait(false));
                    return 0;
                case "answer":
                    {
                        string? file = Option(rest, "--file");
                        bool stdin = rest.Remove("-");
                        List<string[]> choices = Pairs(rest, "--choice"); List<string[]> others = Pairs(rest, "--other"); List<string[]> texts = Pairs(rest, "--text"); List<string[]> confirms = Pairs(rest, "--confirm");
                        List<string> skipped = new List<string>(); string? skip;
                        while ((skip = Option(rest, "--skip-question")) != null) skipped.Add(skip);
                        string id = Target(rest);
                        QuestionAnswerRequest request;
                        if (file != null || stdin)
                        {
                            string json = file == null ? await Console.In.ReadToEndAsync(token).ConfigureAwait(false) : await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
                            request = JsonSerializer.Deserialize<QuestionAnswerRequest>(json, _Json) ?? throw new ArgumentException("Supply an answer JSON object with answers.");
                        }
                        else if (choices.Count + others.Count + texts.Count + confirms.Count + skipped.Count > 0)
                        {
                            request = new QuestionAnswerRequest();
                            foreach (string[] pair in choices) Input(request, pair[0]).Selected = pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                            foreach (string[] pair in others) Input(request, pair[0]).OtherText = pair[1];
                            foreach (string[] pair in texts) Input(request, pair[0]).Text = pair[1];
                            foreach (string[] pair in confirms) Input(request, pair[0]).Confirmed = pair[1] == "yes" || pair[1] == "true" || pair[1] == "y" ? true : pair[1] == "no" || pair[1] == "false" || pair[1] == "n" ? false : throw new ArgumentException("--confirm takes yes or no.");
                            foreach (string key in skipped) Input(request, key).Skip = true;
                        }
                        else
                        {
                            if (Console.IsInputRedirected) throw new ArgumentException("Give answers with --choice, --text, --other or --confirm, or --file. Questions are answered interactively only on a terminal.");
                            request = await PromptAsync(await client.QuestionAsync(id, token).ConfigureAwait(false), token).ConfigureAwait(false);
                        }
                        if (request.IdempotencyKey.Length == 0) request.IdempotencyKey = Guid.NewGuid().ToString("N");
                        Program.Print(await client.AnswerQuestionAsync(id, request, token).ConfigureAwait(false));
                        return 0;
                    }
                case "skip":
                    {
                        string note = Option(rest, "--note") ?? "";
                        Program.Print(await client.SkipQuestionAsync(Target(rest), new QuestionSkipRequest { Note = note, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false));
                        return 0;
                    }
                case "delete":
                    {
                        if (rest.Count == 0) throw new ArgumentException("Use questions delete ID [ID...].");
                        foreach (string id in rest) await client.DeleteQuestionAsync(id, token).ConfigureAwait(false);
                        Program.Print(new Acknowledgement());
                        return 0;
                    }
                case "defer":
                    Program.Print(await client.DeferQuestionAsync(Target(rest), token).ConfigureAwait(false));
                    return 0;
                case "watch":
                    {
                        string cursor = (await client.QuestionEventsAsync("", 1, token).ConfigureAwait(false)).Next;
                        while (!token.IsCancellationRequested)
                        {
                            QuestionEventPage page = await client.QuestionEventsAsync(cursor, 100, token).ConfigureAwait(false);
                            foreach (QuestionEvent entry in page.Items) Console.WriteLine(JsonSerializer.Serialize(entry, _Json));
                            cursor = page.Next;
                            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        }
                        return 0;
                    }
                case "wait":
                    {
                        bool noInput = rest.Remove("--no-input");
                        if (rest.Count != 2) throw new ArgumentException("Use questions wait CONVERSATION RUN [--no-input].");
                        return await WaitAsync(client, rest[0], rest[1], noInput, token).ConfigureAwait(false);
                    }
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        /// <summary>Follow a run until it finishes. When it waits for an answer, ask on a terminal; otherwise print the open question and exit with <see cref="WaitingForInput"/>.</summary>
        private static async Task<int> WaitAsync(ArgueBotClient client, string conversationId, string runId, bool noInput, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                DebateRun run = await client.RunAsync(conversationId, runId, token).ConfigureAwait(false);
                if (run.State == "completed" || run.State == "cancelled" || run.State == "failed" || run.State == "paused") { Program.Print(run); return 0; }
                if (run.State == "awaiting_input")
                {
                    QuestionRequest? open = (await client.RunQuestionsAsync(conversationId, runId, token).ConfigureAwait(false)).FirstOrDefault(item => item.Status == "open" && item.Wait);
                    if (open != null)
                    {
                        if (noInput || Console.IsInputRedirected) { Program.Print(open); Console.Error.WriteLine("The debate is waiting for an answer. Answer it with: questions answer " + open.Id); return WaitingForInput; }
                        QuestionAnswerRequest request = await PromptAsync(open, token).ConfigureAwait(false);
                        request.IdempotencyKey = Guid.NewGuid().ToString("N");
                        try { await client.AnswerQuestionAsync(open.Id, request, token).ConfigureAwait(false); }
                        catch (ArgueBot.Core.Services.DomainException error) when (error.Code == "question_already_resolved") { Console.Error.WriteLine("Someone else already answered this question."); }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
            return 0;
        }

        /// <summary>Ask each question on the terminal. Agent text goes to stderr so stdout stays JSON.</summary>
        private static async Task<QuestionAnswerRequest> PromptAsync(QuestionRequest request, CancellationToken token)
        {
            QuestionAnswerRequest result = new QuestionAnswerRequest();
            TextWriter output = Console.Error;
            await output.WriteLineAsync("Question from " + (request.AskerName.Length == 0 ? request.AskerId : request.AskerName) + (request.ConversationTitle.Length == 0 ? "" : " in \"" + request.ConversationTitle + "\"")).ConfigureAwait(false);
            if (request.Context.Length > 0) await output.WriteLineAsync(request.Context).ConfigureAwait(false);
            if (request.CredentialWarning) await output.WriteLineAsync("Warning: this looks like a request for a password, token or key. ArgueBot never needs secrets in answers.").ConfigureAwait(false);
            foreach (Question question in request.Questions)
            {
                token.ThrowIfCancellationRequested();
                await output.WriteLineAsync().ConfigureAwait(false);
                await output.WriteLineAsync(question.Header + ": " + question.Prompt + (question.Required ? "" : " (optional, Enter to skip)")).ConfigureAwait(false);
                QuestionAnswerInput input = new QuestionAnswerInput { QuestionId = question.Id };
                switch (question.Kind)
                {
                    case "confirm":
                        input.Confirmed = Ask(output, "yes/no") switch { "y" or "yes" => true, "n" or "no" => false, "" when !question.Required => null, _ => throw new ArgumentException("Answer yes or no.") };
                        if (!input.Confirmed.HasValue) input.Skip = true;
                        break;
                    case "text":
                        input.Text = Ask(output, question.Placeholder.Length == 0 ? "answer" : question.Placeholder);
                        if (input.Text.Length == 0 && !question.Required) input.Skip = true;
                        break;
                    default:
                        for (int index = 0; index < question.Options.Count; index++)
                        {
                            QuestionOption option = question.Options[index];
                            await output.WriteLineAsync("  " + (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + option.Label + (option.Recommended ? " (recommended)" : "") + (option.Description.Length == 0 ? "" : " - " + option.Description)).ConfigureAwait(false);
                        }
                        if (question.AllowOther) await output.WriteLineAsync("  0. Other").ConfigureAwait(false);
                        string reply = Ask(output, question.Kind == "multi_choice" ? "numbers separated by commas" : "number");
                        if (reply.Length == 0 && !question.Required) { input.Skip = true; break; }
                        foreach (string part in reply.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 0 || number > question.Options.Count) throw new ArgumentException("Choose a listed number.");
                            if (number == 0 && question.AllowOther) input.OtherText = Ask(output, "Other");
                            else if (number > 0) input.Selected.Add(question.Options[number - 1].Key);
                        }
                        break;
                }
                result.Answers.Add(input);
            }
            return result;
        }

        private static string Ask(TextWriter output, string label)
        {
            output.Write("> " + label + ": ");
            return (Console.ReadLine() ?? "").Trim();
        }

        private static QuestionAnswerInput Input(QuestionAnswerRequest request, string key)
        {
            QuestionAnswerInput? existing = request.Answers.FirstOrDefault(item => item.Key == key);
            if (existing != null) return existing;
            QuestionAnswerInput created = new QuestionAnswerInput { Key = key };
            request.Answers.Add(created);
            return created;
        }

        private static List<string[]> Pairs(List<string> words, string name)
        {
            List<string[]> result = new List<string[]>(); string? value;
            while ((value = Option(words, name)) != null)
            {
                int split = value.IndexOf('=', StringComparison.Ordinal);
                if (split < 1) throw new ArgumentException(name + " takes KEY=VALUE, where KEY is the question key from questions show.");
                result.Add(new[] { value.Substring(0, split), value.Substring(split + 1) });
            }
            return result;
        }

        private static string? Option(List<string> words, string name)
        {
            int index = words.IndexOf(name);
            if (index < 0) return null;
            if (index + 1 >= words.Count) throw new ArgumentException(name + " needs a value.");
            string value = words[index + 1];
            words.RemoveRange(index, 2);
            return value;
        }

        private static string Target(List<string> rest)
        {
            if (rest.Count != 1) throw new ArgumentException("Name one question request by ID (qrq_...). questions list shows them.");
            return rest[0];
        }
    }
}
