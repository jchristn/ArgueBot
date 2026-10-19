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
        private readonly TextField _Question;
        private readonly Dropdown<string> _First;
        private readonly Dropdown<string> _Second;
        private readonly Dropdown<string> _Third;
        private readonly TextField _Rounds = new TextField { Value = "6" };
        private readonly TextField _Seconds = new TextField { Value = "15" };
        internal Form Form { get; } = new Form();
        internal DebateSetupEditor(List<RunnerProfile> profiles, string question, bool existing)
        {
            _Profiles = profiles.Where(profile => profile.Enabled).ToList();
            if (_Profiles.Count < 2) throw new ArgumentException("Configure at least two enabled agent profiles.", nameof(profiles));
            _Question = new TextField { Value = question, IsReadOnly = existing };
            string[] names = _Profiles.Select(profile => profile.Name + " (" + profile.Kind + ")").ToArray();
            _First = new Dropdown<string>(names) { SelectedIndex = 0 };
            _Second = new Dropdown<string>(names) { SelectedIndex = 1 };
            _Third = new Dropdown<string>(new[] { "None: two speakers" }.Concat(names)) { SelectedIndex = 0 };
            Add("Question / prompt", _Question, "The conversation question is the debate prompt. Notes provide additional context.", () => string.IsNullOrWhiteSpace(_Question.Value) || _Question.Value.Length > 4000 ? "Enter a question or prompt up to 4000 characters." : null);
            Form.Add("Opening speaker", _First); Form.SetFieldHint(1, "Speaks first in every round. Configure reusable agents with Ctrl+E outside this form.");
            Form.Add("Second speaker", _Second, () => _Second.SelectedIndex == _First.SelectedIndex ? "Choose a different second speaker." : null); Form.SetFieldHint(2, "Speaks second; closes each round when no third speaker is selected.");
            Form.Add("Third speaker", _Third, () => _Third.SelectedIndex > 0 && (_Third.SelectedIndex - 1 == _First.SelectedIndex || _Third.SelectedIndex - 1 == _Second.SelectedIndex) ? "Choose a different third speaker or None." : null); Form.SetFieldHint(3, "Optional third agent. When selected, this agent speaks last in each round.");
            Add("Maximum rounds", _Rounds, "One through 50. Debate ends on consensus or this round limit.", () => int.TryParse(_Rounds.Value, out int value) && value >= 1 && value <= 50 ? null : "Use one through 50 rounds.");
            Add("Intervention seconds", _Seconds, "Zero through 600 seconds to intervene between turns. Ctrl+R opens run controls.", () => int.TryParse(_Seconds.Value, out int value) && value >= 0 && value <= 600 ? null : "Use zero through 600 seconds.");
        }
        private void Add(string label, TextField field, string hint, Func<string?> validate)
        { Form.Add(label, field, validate); Form.SetFieldHint(Form.FieldCount - 1, hint); }
        internal CreateRunRequest Extract()
        {
            List<string> ids = new List<string> { _Profiles[_First.SelectedIndex].Id, _Profiles[_Second.SelectedIndex].Id };
            if (_Third.SelectedIndex > 0) ids.Add(_Profiles[_Third.SelectedIndex - 1].Id);
            return new CreateRunRequest { Question = _Question.Value, ParticipantIds = ids, MaxRounds = int.Parse(_Rounds.Value, CultureInfo.InvariantCulture), InterventionSeconds = int.Parse(_Seconds.Value, CultureInfo.InvariantCulture), IdempotencyKey = Guid.NewGuid().ToString("N") };
        }
    }
}
