namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using ArgueBot.Core.Models;

    /// <summary>Validation and formatting shared by the ask_user tool, storage and the answer routes.</summary>
    public static class QuestionRules
    {
        /// <summary>Free-text question.</summary>
        public const string Text = "text";
        /// <summary>Exactly one option, or Other.</summary>
        public const string SingleChoice = "single_choice";
        /// <summary>Several options within the selection bounds.</summary>
        public const string MultiChoice = "multi_choice";
        /// <summary>Yes or no.</summary>
        public const string Confirm = "confirm";
        /// <summary>Most questions in one request.</summary>
        public const int MaxQuestions = 4;
        /// <summary>Fewest options in a choice question.</summary>
        public const int MinOptions = 2;
        /// <summary>Most options in a choice question.</summary>
        public const int MaxOptions = 12;
        /// <summary>Longest text answer allowed.</summary>
        public const int MaxTextAnswer = 8000;
        /// <summary>Longest Other text or skip note.</summary>
        public const int MaxNote = 2000;

        private static readonly Regex _Key = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,31}$", RegexOptions.CultureInvariant);
        private static readonly Regex _Credential = new Regex(@"\b(passwords?|passphrases?|passcodes?|api[\s_-]?keys?|access[\s_-]?keys?|secret[\s_-]?keys?|secrets?|tokens?|private[\s_-]?keys?|credentials?|ssh[\s_-]?keys?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Validate and normalize questions the agent wrote. Errors name the field path so the model can correct itself.</summary>
        /// <param name="questions">Questions in the order the agent listed them.</param>
        /// <param name="maximum">Most questions allowed for this request.</param>
        /// <exception cref="DomainException">400 invalid_question with the field path.</exception>
        public static void Validate(List<Question> questions, int maximum = MaxQuestions)
        {
            ArgumentNullException.ThrowIfNull(questions);
            if (questions.Count < 1 || questions.Count > maximum) throw Invalid("Ask 1 through " + maximum.ToString(CultureInfo.InvariantCulture) + " questions in one request.", "questions");
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < questions.Count; index++)
            {
                Question question = questions[index];
                string path = "questions[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (question.Key.Length == 0) question.Key = "q" + (index + 1).ToString(CultureInfo.InvariantCulture);
                if (!_Key.IsMatch(question.Key)) throw Invalid("Use a key of 1 through 32 letters, digits, '.', '_' or '-', starting with a letter or digit.", path + ".key");
                if (!keys.Add(question.Key)) throw Invalid("Each question needs a different key.", path + ".key");
                question.Header = Clean(question.Header);
                question.Prompt = Clean(question.Prompt);
                question.Placeholder = Clean(question.Placeholder);
                if (question.Header.Length == 0) question.Header = Truncate(question.Prompt, 40);
                if (question.Header.Length > 40) throw Invalid("Keep the header to 40 characters.", path + ".header");
                if (question.Prompt.Length == 0 || question.Prompt.Length > 1000) throw Invalid("Write the question in 1 through 1000 characters.", path + ".prompt");
                if (question.Placeholder.Length > 120) throw Invalid("Keep the placeholder to 120 characters.", path + ".placeholder");
                switch (question.Kind)
                {
                    case Text:
                        if (question.Options.Count > 0) throw Invalid("A text question has no options.", path + ".options");
                        if (question.MaxLength == 0) question.MaxLength = MaxTextAnswer;
                        if (question.MaxLength < 1 || question.MaxLength > MaxTextAnswer) throw Invalid("maxLength is 1 through 8000.", path + ".maxLength");
                        question.AllowOther = false; question.MinSelections = 0; question.MaxSelections = 0;
                        break;
                    case Confirm:
                        if (question.Options.Count > 0) throw Invalid("A confirm question has no options; the user answers yes or no.", path + ".options");
                        question.AllowOther = false; question.MinSelections = 0; question.MaxSelections = 0; question.MaxLength = 0;
                        break;
                    case SingleChoice:
                    case MultiChoice:
                        ValidateOptions(question, path);
                        question.MaxLength = 0;
                        if (question.Kind == SingleChoice)
                        {
                            if (question.Options.Count(option => option.Recommended) > 1) throw Invalid("Recommend at most one option of a single choice.", path + ".options");
                            question.MinSelections = question.Required ? 1 : 0; question.MaxSelections = 1;
                        }
                        else
                        {
                            if (question.MaxSelections == 0) question.MaxSelections = question.Options.Count;
                            if (question.MinSelections == 0 && question.Required && !question.AllowOther) question.MinSelections = 1;
                            if (question.MaxSelections < 1 || question.MaxSelections > question.Options.Count) throw Invalid("maxSelections is 1 through the number of options.", path + ".maxSelections");
                            if (question.MinSelections < 0 || question.MinSelections > question.MaxSelections) throw Invalid("minSelections is 0 through maxSelections.", path + ".minSelections");
                        }
                        break;
                    default:
                        throw Invalid("kind is text, single_choice, multi_choice or confirm.", path + ".kind");
                }
            }
        }

        /// <summary>Validate a request's context text.</summary>
        /// <param name="context">Markdown the agent wrote to explain why it asks.</param>
        /// <returns>The trimmed context.</returns>
        /// <exception cref="DomainException">400 invalid_question.</exception>
        public static string ValidateContext(string context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string result = Clean(context);
            if (result.Length > 2000) throw Invalid("Keep context to 2000 characters.", "context");
            return result;
        }

        /// <summary>Whether any question or the context reads like a request for a password, token, key or other secret.</summary>
        /// <param name="context">Request context.</param>
        /// <param name="questions">Questions.</param>
        /// <returns>True when the interfaces should warn more strongly.</returns>
        public static bool LooksLikeCredentialRequest(string context, IEnumerable<Question> questions)
        {
            ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(questions);
            if (_Credential.IsMatch(context)) return true;
            return questions.Any(question => _Credential.IsMatch(question.Header) || _Credential.IsMatch(question.Prompt));
        }

        /// <summary>Check one answer against its question and normalize it into the stored shape.</summary>
        /// <param name="question">Question being answered.</param>
        /// <param name="input">Submitted answer, or null when omitted.</param>
        /// <param name="path">Field path for errors.</param>
        /// <returns>The normalized answer.</returns>
        /// <exception cref="DomainException">400 invalid_answer with the field path.</exception>
        public static QuestionAnswer Conform(Question question, QuestionAnswerInput? input, string path)
        {
            ArgumentNullException.ThrowIfNull(question); ArgumentNullException.ThrowIfNull(path);
            if (input == null || input.Skip)
            {
                if (question.Required) throw InvalidAnswer("Answer this question, or skip the whole request.", path);
                return new QuestionAnswer { Skipped = true };
            }
            QuestionAnswer answer = new QuestionAnswer();
            switch (question.Kind)
            {
                case Text:
                    string text = input.Text.Trim();
                    if (text.Length == 0 && question.Required) throw InvalidAnswer("Write an answer.", path + ".text");
                    if (text.Length > question.MaxLength) throw InvalidAnswer("Keep the answer to " + question.MaxLength.ToString(CultureInfo.InvariantCulture) + " characters.", path + ".text");
                    if (text.Length == 0) answer.Skipped = true;
                    answer.Text = text;
                    break;
                case Confirm:
                    if (!input.Confirmed.HasValue) throw InvalidAnswer("Answer yes or no.", path + ".confirmed");
                    answer.Confirmed = input.Confirmed;
                    break;
                default:
                    List<string> selected = input.Selected.Distinct(StringComparer.Ordinal).ToList();
                    foreach (string key in selected)
                        if (!question.Options.Any(option => option.Key == key)) throw InvalidAnswer("Choose from the listed options.", path + ".selected");
                    string other = input.OtherText.Trim();
                    if (other.Length > 0 && !question.AllowOther) throw InvalidAnswer("This question does not accept Other.", path + ".otherText");
                    if (other.Length > MaxNote) throw InvalidAnswer("Keep Other to 2000 characters.", path + ".otherText");
                    int count = selected.Count + (other.Length > 0 ? 1 : 0);
                    if (question.Kind == SingleChoice && count != 1 && (question.Required || count > 1)) throw InvalidAnswer("Choose exactly one option.", path + ".selected");
                    if (question.Kind == MultiChoice && (count < Math.Max(question.MinSelections, question.Required ? 1 : 0) || selected.Count > question.MaxSelections)) throw InvalidAnswer("Choose " + question.MinSelections.ToString(CultureInfo.InvariantCulture) + " through " + question.MaxSelections.ToString(CultureInfo.InvariantCulture) + " options.", path + ".selected");
                    answer.Selected = question.Options.Where(option => selected.Contains(option.Key, StringComparer.Ordinal)).Select(option => option.Key).ToList();
                    answer.SelectedLabels = question.Options.Where(option => selected.Contains(option.Key, StringComparer.Ordinal)).Select(option => option.Label).ToList();
                    answer.OtherText = other;
                    if (count == 0) answer.Skipped = true;
                    break;
            }
            return answer;
        }

        /// <summary>Render the resolution as the moderator message every visible seat reads. Agent-facing English, not interface text.</summary>
        /// <param name="request">The resolved request with its answers.</param>
        /// <returns>Plain text for the transcript and prompts.</returns>
        public static string Describe(QuestionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            StringBuilder text = new StringBuilder();
            if (request.Status == "skipped" || request.Status == "expired")
            {
                text.Append(request.Status == "expired" ? "The user did not answer in time" : "The user skipped");
                text.Append(" the question").Append(request.Questions.Count == 1 ? "" : "s").Append(" from ").Append(request.AskerName.Length == 0 ? request.AskerId : request.AskerName).Append(". Continue on stated assumptions.");
                if (request.ResolutionNote.Length > 0) text.Append("\nNote from the user: ").Append(request.ResolutionNote);
                foreach (Question question in request.Questions) text.Append("\n- ").Append(question.Header).Append(": ").Append(question.Prompt);
                return text.ToString();
            }
            text.Append("The user answered ").Append(request.AskerName.Length == 0 ? request.AskerId : request.AskerName).Append(':');
            foreach (Question question in request.Questions)
            {
                text.Append("\n- ").Append(question.Header).Append(" (").Append(question.Prompt).Append("): ");
                QuestionAnswer? answer = question.Answer;
                if (answer == null || answer.Skipped) { text.Append("not answered"); continue; }
                List<string> parts = new List<string>(answer.SelectedLabels);
                if (answer.OtherText.Length > 0) parts.Add("Other: " + answer.OtherText);
                if (answer.Text.Length > 0) parts.Add(answer.Text);
                if (answer.Confirmed.HasValue) parts.Add(answer.Confirmed.Value ? "yes" : "no");
                text.Append(string.Join("; ", parts));
            }
            return text.ToString();
        }

        private static void ValidateOptions(Question question, string path)
        {
            if (question.Options.Count < MinOptions || question.Options.Count > MaxOptions) throw Invalid("Offer 2 through 12 options.", path + ".options");
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < question.Options.Count; index++)
            {
                QuestionOption option = question.Options[index];
                string optionPath = path + ".options[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (option.Key.Length == 0) option.Key = "o" + (index + 1).ToString(CultureInfo.InvariantCulture);
                if (!_Key.IsMatch(option.Key)) throw Invalid("Use an option id of 1 through 32 letters, digits, '.', '_' or '-', starting with a letter or digit.", optionPath + ".id");
                if (!keys.Add(option.Key)) throw Invalid("Each option needs a different id.", optionPath + ".id");
                option.Label = Clean(option.Label); option.Description = Clean(option.Description);
                if (option.Label.Length == 0 || option.Label.Length > 80) throw Invalid("Write each option label in 1 through 80 characters.", optionPath + ".label");
                if (option.Description.Length > 300) throw Invalid("Keep option descriptions to 300 characters.", optionPath + ".description");
            }
        }

        private static string Clean(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Contains('\0', StringComparison.Ordinal)) throw Invalid("Text cannot contain NUL characters.", "questions");
            return value.Trim();
        }

        private static string Truncate(string value, int length) { return value.Length <= length ? value : value.Substring(0, length - 1).TrimEnd() + "…"; }

        private static DomainException Invalid(string message, string field) { return new DomainException(400, "invalid_question", message, field); }

        private static DomainException InvalidAnswer(string message, string field) { return new DomainException(400, "invalid_answer", message, field); }
    }
}
