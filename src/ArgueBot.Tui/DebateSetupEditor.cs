namespace ArgueBot.Tui
{
    using ArgueBot.Core.Models;
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
        private readonly TextField _WallClockSeconds = new TextField { Value = "86400" };
        private readonly TextField _ContextCharacters = new TextField { Value = "262144" };
        private readonly TextField _EstimatedInputTokens = new TextField { Value = "0" };
        private readonly TextField _QuestionInstructions = new TextField { Value = "" };
        private readonly TextField _SynthesisInstructions = new TextField { Value = "" };
        private readonly TextField _SummaryInstructions = new TextField { Value = "" };
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
                MaxWallClockSeconds = int.Parse(_WallClockSeconds.Value, CultureInfo.InvariantCulture),
                MaxContextCharacters = int.Parse(_ContextCharacters.Value, CultureInfo.InvariantCulture),
                MaxEstimatedInputTokens = int.Parse(_EstimatedInputTokens.Value, CultureInfo.InvariantCulture),
                QuestionInstructions = _QuestionInstructions.Value,
                SynthesisInstructions = _SynthesisInstructions.Value,
                SummaryInstructions = _SummaryInstructions.Value,
            };
        }
        internal string Review()
        {
            List<string> names = Ordered().Select(profile => profile.Name).ToList();
            string orchestrator = _Orchestrator.SelectedIndex > 0 ? _Profiles[_Orchestrator.SelectedIndex - 1].Name : "Choose an orchestrator";
            return "Question\n" + _Question.Value + "\n\nPanel (" + names.Count + "): " + string.Join(", ", names) + "\nOrchestrator: " + orchestrator + "\nEvery active agent must explicitly agree.\nOpening order rotates each round.\n\nLimit: " + _Rounds.Value + " rounds.\nIntervention window: " + _Seconds.Value + " seconds.\nEach context submission offers Interrupt now or Use in next turn.\n\nBudgets: " + _ParticipantTurns.Value + " participant calls, " + _OrchestrationCalls.Value + " orchestration, " + _CompactionCalls.Value + " compaction, " + _ToolCalls.Value + " tools.\nElapsed deadline: " + _WallClockSeconds.Value + " seconds including pauses.\nContext ceiling: " + _ContextCharacters.Value + " characters. Estimated initial-input cap: " + _EstimatedInputTokens.Value + " (0 disables). No currency cap.\n\nChoose Back to change anything.\nChoose Start debate to run the selected agents.";
        }
    }
}
