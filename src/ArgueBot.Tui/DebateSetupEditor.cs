namespace ArgueBot.Tui
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TUIKit.Widgets;

    internal sealed class DebateSetupEditor
    {
        private readonly List<RunnerProfile> _Profiles;
        private readonly string _CommandKey = Guid.NewGuid().ToString("N");
        private readonly TextField _Question;
        private readonly List<Checkbox> _Seats = new List<Checkbox>();
        private readonly Dropdown<string> _Orchestrator;
        private readonly int _MaximumParticipants;
        private readonly TextField _Order = new TextField();
        private readonly TextField _Rounds = new TextField { Value = "6" };
        private readonly TextField _Seconds = new TextField { Value = "15" };
        private readonly TextField _ParticipantTurns = new TextField { Value = "8192" };
        private readonly TextField _OrchestrationCalls = new TextField { Value = "256" };
        private readonly TextField _CompactionCalls = new TextField { Value = "256" };
        private readonly TextField _ToolCalls = new TextField { Value = "8192" };
        private readonly TextField _StepRetries = new TextField { Value = "3" };
        private readonly TextField _WallClockSeconds = new TextField { Value = "86400" };
        private readonly TextField _ContextCharacters = new TextField { Value = "262144" };
        private readonly TextField _EstimatedInputTokens = new TextField { Value = "0" };
        private readonly TextField _QuestionInstructions = new TextField { Value = "" };
        private readonly TextField _SynthesisInstructions = new TextField { Value = "" };
        private readonly TextField _SummaryInstructions = new TextField { Value = "" };
        private static readonly string[] _AccessModes = { "read", "read_write", "off" };
        private readonly Dropdown<string> _ResourceAccess = new Dropdown<string>(new[] { "Read: list, search and read resources", "Read and write: also create, update and append", "Off: no resource tools" }) { SelectedIndex = 0 };
        private readonly TextField _ResourceNames = new TextField { Value = "" };
        private readonly TextField _ResourceTags = new TextField { Value = "" };
        private readonly TextField _ResourceWrites = new TextField { Value = "8" };
        private readonly TextField _ResourceBytes = new TextField { Value = "65536" };
        private readonly TextField _ResourcesCreated = new TextField { Value = "50" };
        private static readonly string[] _AskModes = { "all", "orchestrator", "off" };
        private readonly Dropdown<string> _UserQuestions = new Dropdown<string>(new[] { "Every agent may ask you questions", "Only the orchestrator may ask", "Nobody asks; agents state assumptions" }) { SelectedIndex = 0 };
        private readonly Dropdown<string> _Intake = new Dropdown<string>(new[] { "Ask about a vague question before round 1", "Start the debate at once" }) { SelectedIndex = 0 };
        private readonly TextField _HoldSeconds = new TextField { Value = "300" };
        private readonly TextField _ExpirySeconds = new TextField { Value = "0" };
        private readonly Dropdown<string> _ResumeOnAnswer = new Dropdown<string>(new[] { "Continue the debate", "Stay paused until I resume" }) { SelectedIndex = 0 };
        private readonly TextField _QuestionsPerTurn = new TextField { Value = "2" };
        private readonly TextField _QuestionsPerRun = new TextField { Value = "20" };
        private readonly TextField _IntakeQuestions = new TextField { Value = "4" };
        internal bool ExistingConversation { get; }
        internal Form QuestionForm { get; } = new Form();
        internal Form SpeakersForm { get; } = new Form();
        internal Form SettingsForm { get; } = new Form();
        internal DebateSetupEditor(List<RunnerProfile> profiles, string question, bool existing, int maximumParticipants = 16)
        {
            _Profiles = profiles.Where(profile => profile.Enabled).ToList();
            if (_Profiles.Count < 2) throw new ArgumentException("Configure at least two enabled agents.", nameof(profiles));
            _MaximumParticipants = maximumParticipants;
            ExistingConversation = existing;
            _Question = new TextField { Value = question, Placeholder = "Enter a new debate question" };
            string[] names = _Profiles.Select(profile => profile.Name + " (" + profile.Kind + ")").ToArray();
            _Orchestrator = new Dropdown<string>(new[] { "Choose the model ArgueBot should use" }.Concat(names)) { SelectedIndex = 0 };
            QuestionForm.Add("What should the agents debate?", _Question, () => string.IsNullOrWhiteSpace(_Question.Value) || _Question.Value.Length > 4000 ? "Enter a question up to 4000 characters." : null);
            QuestionForm.SetFieldHint(0, existing ? "Enter a new question for this conversation. Existing history remains context. Review before starting." : "Enter your question, then choose Next. Nothing starts until Review.");
            for (int index = 0; index < _Profiles.Count; index++)
            {
                Checkbox selected = new Checkbox(names[index], index < 2); _Seats.Add(selected);
                SpeakersForm.Add("Seat " + (index + 1), selected, ValidateSeats);
            }
            SpeakersForm.SetFieldHint(0, "Select the agents for this panel. Each must agree on the same final proposal. The opening speaker rotates between rounds.");
            SpeakersForm.Add("Orchestrator model", _Orchestrator, () => _Orchestrator.SelectedIndex <= 0 ? "Choose the model ArgueBot uses for questions and summaries." : null);
            SettingsForm.Add("Maximum rounds", _Rounds, () => int.TryParse(_Rounds.Value, out int value) && value >= 1 && value <= 50 ? null : "Use one through 50 rounds."); SettingsForm.SetFieldHint(0, "Debate ends on consensus or this limit (1 to 50 rounds).");
            SettingsForm.Add("Time to intervene (seconds)", _Seconds, () => int.TryParse(_Seconds.Value, out int value) && value >= 0 && value <= 600 ? null : "Use zero through 600 seconds."); SettingsForm.SetFieldHint(1, "Pause between turns (0 to 600). Run controls can interrupt later.");
            SettingsForm.Add("Opening seat order (optional)", _Order, ValidateOrder);
            SettingsForm.SetFieldHint(2, "Leave empty for the panel order shown earlier, or list every checked seat number, e.g. 3,1,2. Opening order rotates each round.");
            SettingsForm.Add("Participant invocations (answers and votes)", _ParticipantTurns, () => int.TryParse(_ParticipantTurns.Value, out int value) && value >= 1 && value <= 100000 ? null : "Use 1 through 100000.");
            SettingsForm.Add("Orchestrator invocations", _OrchestrationCalls, () => int.TryParse(_OrchestrationCalls.Value, out int value) && value >= 1 && value <= 10000 ? null : "Use 1 through 10000.");
            SettingsForm.Add("Compaction invocations", _CompactionCalls, () => int.TryParse(_CompactionCalls.Value, out int value) && value >= 1 && value <= 10000 ? null : "Use 1 through 10000.");
            SettingsForm.Add("Tool-call limit", _ToolCalls, () => int.TryParse(_ToolCalls.Value, out int value) && value >= 1 && value <= 100000 ? null : "Use 1 through 100000.");
            SettingsForm.Add("Elapsed deadline (seconds)", _WallClockSeconds, () => int.TryParse(_WallClockSeconds.Value, out int value) && value >= 1 && value <= 604800 ? null : "Use 1 through 604800.");
            SettingsForm.Add("Context ceiling (characters)", _ContextCharacters, () => int.TryParse(_ContextCharacters.Value, out int value) && value >= 8192 && value <= 16777216 ? null : "Use 8192 through 16777216.");
            SettingsForm.Add("Estimated input tokens (0 = no limit)", _EstimatedInputTokens, () => int.TryParse(_EstimatedInputTokens.Value, out int value) && value >= 0 && value <= 2147483647 ? null : "Use 0 through 2147483647.");
            SettingsForm.Add("Additional question guidance", _QuestionInstructions, () => _QuestionInstructions.Value.Length <= 4000 ? null : "Use up to 4000 characters.");
            SettingsForm.Add("Additional proposal guidance", _SynthesisInstructions, () => _SynthesisInstructions.Value.Length <= 4000 ? null : "Use up to 4000 characters.");
            SettingsForm.Add("Additional memory guidance", _SummaryInstructions, () => _SummaryInstructions.Value.Length <= 4000 ? null : "Use up to 4000 characters.");
            SettingsForm.Add("Resource access", _ResourceAccess);
            SettingsForm.Add("Only these resources (optional)", _ResourceNames, () => List(_ResourceNames.Value).All(name => ResourceRules.TryNormalizeName(name, out _)) ? null : "List resource names separated by commas, for example billing, team/conventions.");
            SettingsForm.Add("Or resources tagged (optional)", _ResourceTags, () => List(_ResourceTags.Value).Count <= 16 ? null : "Use at most 16 tags.");
            SettingsForm.Add("Resource writes per turn", _ResourceWrites, () => int.TryParse(_ResourceWrites.Value, out int value) && value >= 0 && value <= 100 ? null : "Use 0 through 100.");
            SettingsForm.Add("Resource bytes written per turn", _ResourceBytes, () => int.TryParse(_ResourceBytes.Value, out int value) && value >= 0 && value <= 1048576 ? null : "Use 0 through 1048576.");
            SettingsForm.Add("Resources created per debate", _ResourcesCreated, () => int.TryParse(_ResourcesCreated.Value, out int value) && value >= 0 && value <= 1000 ? null : "Use 0 through 1000.");
            SettingsForm.Add("Automatic retries per step", _StepRetries, () => int.TryParse(_StepRetries.Value, out int value) && value >= 0 && value <= 10 ? null : "Use 0 through 10. 0 pauses at the first failure.");
            SettingsForm.Add("Questions for you", _UserQuestions);
            SettingsForm.Add("Opening questions", _Intake);
            SettingsForm.Add("Seconds an agent waits for your answer", _HoldSeconds, () => int.TryParse(_HoldSeconds.Value, out int value) && value >= 0 && value <= 1800 ? null : "Use 0 through 1800.");
            SettingsForm.Add("Seconds before a question expires (0 = never)", _ExpirySeconds, () => int.TryParse(_ExpirySeconds.Value, out int value) && value >= 0 && value <= 2592000 ? null : "Use 0 through 2592000.");
            SettingsForm.Add("After you answer", _ResumeOnAnswer);
            SettingsForm.Add("Questions per agent turn", _QuestionsPerTurn, () => int.TryParse(_QuestionsPerTurn.Value, out int value) && value >= 0 && value <= 4 ? null : "Use 0 through 4.");
            SettingsForm.Add("Questions per debate", _QuestionsPerRun, () => int.TryParse(_QuestionsPerRun.Value, out int value) && value >= 0 && value <= 200 ? null : "Use 0 through 200.");
            SettingsForm.Add("Opening questions at most", _IntakeQuestions, () => int.TryParse(_IntakeQuestions.Value, out int value) && value >= 1 && value <= 4 ? null : "Use 1 through 4.");
            SettingsForm.SetFieldHint(20, "Agents can stop mid-turn to ask you questions and wait for the answer. You answer in a dialog; closing it never skips.");
            SettingsForm.SetFieldHint(21, "Before round 1 the orchestrator may ask about a vague question. Uses one orchestrator invocation.");
            SettingsForm.SetFieldHint(22, "After this the agent's turn ends and picks up again when you answer (0 to 1800; 0 ends it at once).");
            SettingsForm.SetFieldHint(13, "What agents may do with this tenant's resources (named markdown documents). Write limits apply only to Read and write.");
            SettingsForm.SetFieldHint(14, "Leave both lists empty to let agents see every resource. Resources they create stay visible to them.");
            SettingsForm.SetFieldHint(19, "Repeats of one step before the debate pauses: correction requests after an invalid reply, and repeats after a rate limit, outage, dropped connection, agent exit or timeout (waits 5, 15, 45 s...). A repeat can repeat external work.");
            SettingsForm.SetFieldHint(7, "Elapsed time includes queues and pauses. Retries do not reset any budget.");
            SettingsForm.SetFieldHint(9, "Initial-input UTF-8 estimate, not billed tokens; excludes continuations/output. Provider pricing is unavailable.");
        }
        private string? ValidateSeats()
        {
            int count = _Seats.Count(seat => seat.Checked);
            if (count < 2 || count > _MaximumParticipants) return "Choose 2 through " + _MaximumParticipants + " agents.";
            string? duplicate = _Profiles.Where((profile, index) => _Seats[index].Checked).GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).FirstOrDefault();
            return duplicate == null ? null : "Two checked agents are named " + duplicate + ". Rename one so votes stay attributable.";
        }
        private string? ValidateOrder()
        {
            if (string.IsNullOrWhiteSpace(_Order.Value)) return null;
            string[] parts = _Order.Value.Split(','); List<int> indexes = new List<int>();
            foreach (string part in parts)
            {
                if (!int.TryParse(part.Trim(), out int seat) || seat < 1 || seat > _Profiles.Count || !_Seats[seat - 1].Checked || indexes.Contains(seat - 1)) return "List each checked seat number exactly once.";
                indexes.Add(seat - 1);
            }
            return indexes.Count == _Seats.Count(seat => seat.Checked) ? null : "List each checked seat number exactly once.";
        }
        private List<RunnerProfile> Ordered()
        {
            if (string.IsNullOrWhiteSpace(_Order.Value)) return _Profiles.Where((profile, index) => _Seats[index].Checked).ToList();
            return _Order.Value.Split(',').Select(value => _Profiles[int.Parse(value.Trim(), CultureInfo.InvariantCulture) - 1]).ToList();
        }
        internal CreateRunRequest Extract()
        {
            List<string> ids = Ordered().Select(profile => profile.Id).ToList();
            return new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = _Profiles[_Orchestrator.SelectedIndex - 1].Id, Question = _Question.Value, ParticipantIds = ids, MaxRounds = int.Parse(_Rounds.Value, CultureInfo.InvariantCulture), InterventionSeconds = int.Parse(_Seconds.Value, CultureInfo.InvariantCulture), IdempotencyKey = _CommandKey, Policy = Policy() };
        }
        private DebatePolicy Policy()
        {
            return new DebatePolicy
            {
                MaxParticipantTurns = int.Parse(_ParticipantTurns.Value, CultureInfo.InvariantCulture),
                MaxOrchestrationCalls = int.Parse(_OrchestrationCalls.Value, CultureInfo.InvariantCulture),
                MaxCompactionCalls = int.Parse(_CompactionCalls.Value, CultureInfo.InvariantCulture),
                MaxToolCalls = int.Parse(_ToolCalls.Value, CultureInfo.InvariantCulture),
                MaxStepRetries = int.Parse(_StepRetries.Value, CultureInfo.InvariantCulture),
                MaxWallClockSeconds = int.Parse(_WallClockSeconds.Value, CultureInfo.InvariantCulture),
                MaxContextCharacters = int.Parse(_ContextCharacters.Value, CultureInfo.InvariantCulture),
                MaxEstimatedInputTokens = int.Parse(_EstimatedInputTokens.Value, CultureInfo.InvariantCulture),
                QuestionInstructions = _QuestionInstructions.Value,
                SynthesisInstructions = _SynthesisInstructions.Value,
                SummaryInstructions = _SummaryInstructions.Value,
                ResourceAccess = _AccessModes[Math.Max(0, _ResourceAccess.SelectedIndex)],
                ResourceNames = List(_ResourceNames.Value),
                ResourceTags = List(_ResourceTags.Value),
                MaxResourceWritesPerTurn = int.Parse(_ResourceWrites.Value, CultureInfo.InvariantCulture),
                MaxResourceWriteBytesPerTurn = int.Parse(_ResourceBytes.Value, CultureInfo.InvariantCulture),
                MaxResourcesCreated = int.Parse(_ResourcesCreated.Value, CultureInfo.InvariantCulture),
                UserQuestions = _AskModes[Math.Max(0, _UserQuestions.SelectedIndex)],
                Intake = _Intake.SelectedIndex == 1 ? "off" : "ask_if_needed",
                QuestionHoldSeconds = int.Parse(_HoldSeconds.Value, CultureInfo.InvariantCulture),
                QuestionExpirySeconds = int.Parse(_ExpirySeconds.Value, CultureInfo.InvariantCulture),
                ResumeOnAnswer = _ResumeOnAnswer.SelectedIndex != 1,
                MaxQuestionRequestsPerTurn = int.Parse(_QuestionsPerTurn.Value, CultureInfo.InvariantCulture),
                MaxQuestionRequestsPerRun = int.Parse(_QuestionsPerRun.Value, CultureInfo.InvariantCulture),
                MaxIntakeQuestions = int.Parse(_IntakeQuestions.Value, CultureInfo.InvariantCulture),
            };
        }
        private static List<string> List(string text) { return text.Split(',').Select(item => item.Trim().ToLowerInvariant()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToList(); }
        internal string Review()
        {
            List<string> names = Ordered().Select(profile => profile.Name).ToList();
            string orchestrator = _Orchestrator.SelectedIndex > 0 ? _Profiles[_Orchestrator.SelectedIndex - 1].Name : "Choose an orchestrator";
            return "Question\n" + _Question.Value + "\n\nPanel (" + names.Count + "): " + string.Join(", ", names) + "\nOrchestrator: " + orchestrator + "\nEvery active agent must explicitly agree.\nOpening order rotates each round.\n\nLimit: " + _Rounds.Value + " rounds.\nIntervention window: " + _Seconds.Value + " seconds.\nEach context submission offers Interrupt now or Use in next turn.\n\nBudgets: " + _ParticipantTurns.Value + " participant calls, " + _OrchestrationCalls.Value + " orchestration, " + _CompactionCalls.Value + " compaction, " + _ToolCalls.Value + " tools.\nElapsed deadline: " + _WallClockSeconds.Value + " seconds including pauses, not counting time waiting for your answers.\nContext ceiling: " + _ContextCharacters.Value + " characters. Estimated initial-input cap: " + _EstimatedInputTokens.Value + " (0 disables). No currency cap.\nResources: " + _AccessModes[Math.Max(0, _ResourceAccess.SelectedIndex)] + (List(_ResourceNames.Value).Count + List(_ResourceTags.Value).Count == 0 ? ", every resource" : ", only " + string.Join(", ", List(_ResourceNames.Value).Concat(List(_ResourceTags.Value).Select(tag => "tag " + tag)))) + ".\nQuestions for you: " + _AskModes[Math.Max(0, _UserQuestions.SelectedIndex)] + (_Intake.SelectedIndex == 1 ? ", no opening questions" : ", opening questions if the question is vague") + ", agents wait " + _HoldSeconds.Value + " seconds for an answer" + (_ResumeOnAnswer.SelectedIndex == 1 ? ", then the debate stays paused after you answer" : "") + ".\n\nChoose Back to change anything.\nChoose Start debate to run the selected agents.";
        }
    }
}
