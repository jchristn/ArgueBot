namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using ArgueBot.Core.Models;
    using TUIKit.Widgets;

    internal sealed class ParticipantEditor
    {
        private readonly string _Id;
        private readonly Form _Form = new Form();
        private readonly TextField _Name = new TextField();
        private readonly Dropdown<string> _Kind = new Dropdown<string>(new[] { "cli", "rest" });
        private readonly Dropdown<string> _Provider;
        private readonly TextField _Executable = new TextField();
        private readonly TextField _Directory = new TextField();
        private readonly TextField _Endpoint = new TextField();
        private readonly TextField _Model = new TextField();
        private readonly TextField _Secret = new TextField();
        private readonly TextField _Timeout = new TextField();
        private readonly TextField _Output = new TextField();
        private readonly Checkbox _Enabled = new Checkbox("Enabled for new runs", true);
        private readonly List<TextField> _Arguments = new List<TextField>();
        internal Form Form { get { return _Form; } }
        internal ParticipantEditor(RunnerProfile? profile = null)
        {
            profile ??= new RunnerProfile(); _Id = profile.Id;
            _Name.Value = profile.Name; _Kind.SelectedIndex = profile.Kind == "rest" ? 1 : 0;
            _Provider = new Dropdown<string>(Providers()); _Provider.SelectedIndex = Array.IndexOf(Providers(), profile.Provider);
            if (_Provider.SelectedIndex < 0) _Provider.SelectedIndex = 0;
            _Executable.Value = profile.Executable; _Directory.Value = profile.WorkingDirectory; _Endpoint.Value = profile.Endpoint; _Model.Value = profile.Model; _Secret.Value = profile.SecretEnvironmentVariable;
            _Timeout.Value = profile.TimeoutSeconds.ToString(CultureInfo.InvariantCulture); _Output.Value = profile.MaxOutputBytes.ToString(CultureInfo.InvariantCulture); _Enabled.Checked = profile.Enabled;
            foreach (string argument in profile.Arguments) _Arguments.Add(new TextField { Value = argument });
            _Kind.SelectionChanged += (_, _) => { _Provider.SetItems(Providers()); _Provider.SelectedIndex = 0; Rebuild(); };
            Rebuild();
        }
        private string[] Providers() { return _Kind.SelectedIndex == 1 ? new[] { "ollama", "openai", "anthropic", "gemini" } : new[] { "plain", "claude", "codex", "gemini" }; }
        private void Add(string label, TextField field, string hint, Func<string?>? validate = null)
        { int index = _Form.FieldCount; _Form.Add(label, field, validate); _Form.SetFieldHint(index, hint); }
        private void Rebuild()
        {
            _Form.Clear();
            Add("Name", _Name, "Display name retained in historical participant snapshots.", () => string.IsNullOrWhiteSpace(_Name.Value) || _Name.Value.Length > 200 ? "Use one to 200 characters." : null);
            _Form.Add("Runner", _Kind); _Form.SetFieldHint(1, "CLI executes on the service machine; REST uses a PolyPrompt completion adapter.");
            _Form.Add("Protocol/provider", _Provider); _Form.SetFieldHint(2, "Choose the installed CLI output protocol or REST provider contract.");
            if (_Kind.SelectedIndex == 0)
            {
                Add("Executable", _Executable, "Direct executable or script host. Prompts go to stdin; no shell interpolation.", () => string.IsNullOrWhiteSpace(_Executable.Value) ? "An executable is required." : null);
                Add("Working directory", _Directory, "Optional directory on the service machine, validated by the service.");
                _Form.AddSection("Ordered literal arguments");
                for (int index = 0; index < _Arguments.Count; index++)
                {
                    int argument = index;
                    Add("Argument " + (index + 1), _Arguments[index], "An empty field is an intentional empty argument. Order is preserved.");
                    _Form.Add("", new Button("Remove argument " + (index + 1), () => { _Arguments.RemoveAt(argument); Rebuild(); }));
                }
                _Form.Add("", new Button("Add argument", () => { if (_Arguments.Count < 128) { _Arguments.Add(new TextField()); Rebuild(); _Form.SetFocusedField(5 + (_Arguments.Count - 1) * 2); } }));
            }
            else
            {
                Add("Endpoint", _Endpoint, "Absolute HTTP(S) base URL, without URL credentials, query or fragment.", () => Uri.TryCreate(_Endpoint.Value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https") && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? null : "Use an HTTP(S) endpoint without credentials or query.");
                Add("Model", _Model, "Exact model identifier accepted by the selected endpoint.", () => string.IsNullOrWhiteSpace(_Model.Value) ? "A model is required." : null);
                Add("Credential environment reference", _Secret, "Name of a variable on the service machine. Enter its name, never its secret value.", () => _Secret.Value.Length == 0 || Regex.IsMatch(_Secret.Value, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant) ? null : "Use an environment variable name.");
            }
            Add("Timeout seconds", _Timeout, "Generation deadline, from one through 3600 seconds.", () => int.TryParse(_Timeout.Value, out int value) && value >= 1 && value <= 3600 ? null : "Use one through 3600.");
            Add("Output byte limit", _Output, "Bounded stdout/REST answer capture, 1024 through 16777216 bytes.", () => int.TryParse(_Output.Value, out int value) && value >= 1024 && value <= 16777216 ? null : "Use 1024 through 16777216.");
            _Form.Add("", _Enabled); _Form.SetFieldHint(_Form.FieldCount - 1, "Disabling prevents new runs from choosing this profile; historical snapshots remain unchanged.");
        }
        internal RunnerProfile Extract()
        {
            bool rest = _Kind.SelectedIndex == 1;
            return new RunnerProfile { Id = _Id, Name = _Name.Value, Kind = rest ? "rest" : "cli", Provider = _Provider.SelectedItem ?? "plain", Endpoint = rest ? _Endpoint.Value : "", Model = rest ? _Model.Value : "", Executable = rest ? "" : _Executable.Value, WorkingDirectory = rest ? "" : _Directory.Value, Arguments = rest ? new List<string>() : _Arguments.Select(field => field.Value).ToList(), SecretEnvironmentVariable = rest ? _Secret.Value : "", TimeoutSeconds = int.Parse(_Timeout.Value, CultureInfo.InvariantCulture), MaxOutputBytes = int.Parse(_Output.Value, CultureInfo.InvariantCulture), Enabled = _Enabled.Checked };
        }
    }
}
