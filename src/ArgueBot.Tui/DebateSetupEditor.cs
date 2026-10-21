namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class DebateSetupEditor
    {
        private readonly List<RunnerProfile> _Profiles;
        private readonly string _CommandKey = Guid.NewGuid().ToString("N");
        private readonly TextField _Question;
        private readonly Dropdown<string> _First;
        private readonly Dropdown<string> _Second;
        private readonly Dropdown<string> _Third;
        private readonly TextField _Rounds = new TextField { Value = "6" };
        private readonly TextField _Seconds = new TextField { Value = "15" };
        internal bool ExistingConversation { get; }
        internal Form QuestionForm { get; } = new Form();
        internal Form SpeakersForm { get; } = new Form();
        internal Form SettingsForm { get; } = new Form();
        internal DebateSetupEditor(List<RunnerProfile> profiles, string question, bool existing)
        {
            _Profiles = profiles.Where(profile => profile.Enabled).ToList();
            if (_Profiles.Count < 2) throw new ArgumentException("Configure at least two enabled agents.", nameof(profiles));
            ExistingConversation = existing;
            _Question = new TextField { Value = question, Placeholder = "Enter a new debate question" };
            string[] names = _Profiles.Select(profile => profile.Name + " (" + profile.Kind + ")").ToArray();
            _First = new Dropdown<string>(names) { SelectedIndex = 0 };
            _Second = new Dropdown<string>(names) { SelectedIndex = 1 };
            _Third = new Dropdown<string>(new[] { "None: two speakers" }.Concat(names)) { SelectedIndex = 0 };
            QuestionForm.Add("What should the agents debate?", _Question, () => string.IsNullOrWhiteSpace(_Question.Value) || _Question.Value.Length > 4000 ? "Enter a question up to 4000 characters." : null);
            QuestionForm.SetFieldHint(0, existing ? "Enter a new question for this conversation. Existing history remains context. Review before starting." : "Enter your question, then choose Next. Nothing starts until Review.");
            SpeakersForm.Add("Opening agent", _First); SpeakersForm.SetFieldHint(0, "Speaks first in every round. Enter opens the list; arrows choose.");
            SpeakersForm.Add("Second agent", _Second, () => _Second.SelectedIndex == _First.SelectedIndex ? "Choose a different second agent." : null); SpeakersForm.SetFieldHint(1, "Speaks second, and last if there is no third agent.");
            SpeakersForm.Add("Third agent (optional)", _Third, () => _Third.SelectedIndex > 0 && (_Third.SelectedIndex - 1 == _First.SelectedIndex || _Third.SelectedIndex - 1 == _Second.SelectedIndex) ? "Choose a different third agent or None." : null); SpeakersForm.SetFieldHint(2, "Choose None for two speakers. A third agent closes each round.");
            SettingsForm.Add("Maximum rounds", _Rounds, () => int.TryParse(_Rounds.Value, out int value) && value >= 1 && value <= 50 ? null : "Use one through 50 rounds."); SettingsForm.SetFieldHint(0, "Debate ends on consensus or this limit (1 to 50 rounds).");
            SettingsForm.Add("Time to intervene (seconds)", _Seconds, () => int.TryParse(_Seconds.Value, out int value) && value >= 0 && value <= 600 ? null : "Use zero through 600 seconds."); SettingsForm.SetFieldHint(1, "Pause between turns (0 to 600). Run controls can interrupt later.");
        }
        internal CreateRunRequest Extract()
        {
            List<string> ids = new List<string> { _Profiles[_First.SelectedIndex].Id, _Profiles[_Second.SelectedIndex].Id };
            if (_Third.SelectedIndex > 0) ids.Add(_Profiles[_Third.SelectedIndex - 1].Id);
            return new CreateRunRequest { Question = _Question.Value, ParticipantIds = ids, MaxRounds = int.Parse(_Rounds.Value, CultureInfo.InvariantCulture), InterventionSeconds = int.Parse(_Seconds.Value, CultureInfo.InvariantCulture), IdempotencyKey = _CommandKey };
        }
        internal string Review()
        {
            List<string> names = new List<string> { _Profiles[_First.SelectedIndex].Name, _Profiles[_Second.SelectedIndex].Name };
            if (_Third.SelectedIndex > 0) names.Add(_Profiles[_Third.SelectedIndex - 1].Name);
            return "Question\n" + _Question.Value + "\n\nSpeaking order: " + string.Join(" -> ", names) + "\nOpens each round: " + names[0] + "\nCloses each round: " + names[names.Count - 1] + "\n\nEnds on consensus or " + _Rounds.Value + " rounds.\nIntervention window: " + _Seconds.Value + " seconds.\n\nChoose Back to change anything.\nChoose Start debate to run the selected agents.";
        }
    }
}
