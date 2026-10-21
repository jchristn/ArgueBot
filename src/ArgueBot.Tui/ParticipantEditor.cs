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
            profile ??= HarnessDefaults.Create("claude"); HarnessDefaults.ApplyMissing(profile); _Id = profile.Id;
            _Name.Value = profile.Name; _Kind.SelectedIndex = profile.Kind == "rest" ? 1 : 0;
            _Provider = new Dropdown<string>(ProviderLabels()); _Provider.SelectedIndex = Array.IndexOf(Providers(), profile.Provider);
            if (_Provider.SelectedIndex < 0) _Provider.SelectedIndex = 0;
            _Executable.Value = profile.Executable; _Directory.Value = profile.WorkingDirectory; _Endpoint.Value = profile.Endpoint; _Model.Value = profile.Model; _Secret.Value = profile.SecretEnvironmentVariable;
            _Timeout.Value = profile.TimeoutSeconds.ToString(CultureInfo.InvariantCulture); _Output.Value = profile.MaxOutputBytes.ToString(CultureInfo.InvariantCulture); _Enabled.Checked = profile.Enabled;
            foreach (string argument in profile.Arguments) _Arguments.Add(new TextField { Value = argument });
            _Kind.SelectionChanged += (_, _) => { _Provider.SetItems(ProviderLabels()); _Provider.SelectedIndex = 0; Rebuild(); };
            _Provider.SelectionChanged += (_, _) =>
            {
                if (_Kind.SelectedIndex == 0)
                {
                    RunnerProfile defaults = HarnessDefaults.Create(Providers()[Math.Max(0, _Provider.SelectedIndex)]);
                    _Executable.Value = defaults.Executable;
                    _Arguments.Clear();
                    foreach (string argument in defaults.Arguments) _Arguments.Add(new TextField { Value = argument });
                    if (string.IsNullOrWhiteSpace(_Name.Value) || new[] { "plain", "claude", "codex", "gemini", "mux" }.Any(provider => HarnessDefaults.Create(provider).Name == _Name.Value)) _Name.Value = defaults.Name;
                }
                Rebuild();
            };
            Rebuild();
        }
        private string[] Providers() { return _Kind.SelectedIndex == 1 ? new[] { "ollama", "openai", "anthropic", "gemini" } : new[] { "plain", "claude", "codex", "gemini", "mux" }; }
        private string[] ProviderLabels() { return _Kind.SelectedIndex == 1 ? Providers() : Providers().Select(provider => HarnessDefaults.Create(provider).Name).ToArray(); }
        private void Add(string label, TextField field, string hint, Func<string?>? validate = null)
        { int index = _Form.FieldCount; _Form.Add(label, field, validate); _Form.SetFieldHint(index, hint); }
        private void Rebuild()
        {
            _Form.Clear();
            Add("Name", _Name, "Display name retained in historical participant snapshots.", () => string.IsNullOrWhiteSpace(_Name.Value) || _Name.Value.Length > 200 ? "Use one to 200 characters." : null);
            _Form.Add("Runner", _Kind); _Form.SetFieldHint(1, "CLI executes on the service machine; REST uses a PolyPrompt completion adapter.");
            _Form.Add("Harness / REST provider", _Provider); _Form.SetFieldHint(2, "Claude Code, Codex, Gemini CLI and Mux fill the executable and complete arguments automatically. Generic CLI requires your own configuration.");
            if (_Kind.SelectedIndex == 0)
            {
                Add("Executable (program only)", _Executable, "Enter claude or codex, or the full program path. Do not include flags or wrap the path in quotes. Put flags in the Argument rows below.", () => string.IsNullOrWhiteSpace(_Executable.Value) ? "Enter a program name or path." : Regex.IsMatch(_Executable.Value, @"^\s*(claude|codex|gemini|mux)(\.exe)?\s+-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "Enter the program only, such as claude. Move flags to separate Argument rows." : null);
                Add("Working directory", _Directory, "Optional directory on the service machine, validated by the service.");
                _Form.AddSection("Arguments: one flag or value per row");
                for (int index = 0; index < _Arguments.Count; index++)
                {
                    int argument = index;
                    Add("Argument " + (index + 1), _Arguments[index], "One flag or value per row, e.g. --output-format in one row and stream-json in the next. Custom rows replace all defaults. Empty rows are passed as empty arguments.");
                    _Form.Add("", new Button("Remove argument " + (index + 1), () => { _Arguments.RemoveAt(argument); Rebuild(); }));
                }
                _Form.Add("", new Button("Add argument", () => { if (_Arguments.Count < 128) { _Arguments.Add(new TextField()); Rebuild(); _Form.SetFocusedField(5 + (_Arguments.Count - 1) * 2); } }));
                _Form.SetFieldHint(_Form.FieldCount - 1, "Harness selection fills all arguments. Claude Code includes --dangerously-skip-permissions; Codex includes --yolo for unattended turns. Custom rows replace the full list; no rows restores defaults when saved. Each row is one flag or value.");
            }
            else
            {
                Add("Endpoint", _Endpoint, "Absolute HTTP(S) base URL, without URL credentials, query or fragment.", () => Uri.TryCreate(_Endpoint.Value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https") && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? null : "Use an HTTP(S) endpoint without credentials or query.");
                Add("Working directory", _Directory, "Local tools use this directory on the service machine. Empty uses the service working directory. Files and commands run as the service account.");
                Add("Model", _Model, "Exact model identifier accepted by the selected endpoint.", () => string.IsNullOrWhiteSpace(_Model.Value) ? "A model is required." : null);
                Add("Credential environment reference", _Secret, "Name of a variable on the service machine. Enter its name, never its secret value.", () => _Secret.Value.Length == 0 || Regex.IsMatch(_Secret.Value, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant) ? null : "Use an environment variable name.");
            }
            Add("Timeout seconds", _Timeout, "Generation deadline, from one through 3600 seconds.", () => int.TryParse(_Timeout.Value, out int value) && value >= 1 && value <= 3600 ? null : "Use one through 3600.");
            Add("Output byte limit", _Output, "Bounded stdout/REST answer capture, 1024 through 67108864 bytes.", () => int.TryParse(_Output.Value, out int value) && value >= 1024 && value <= 67108864 ? null : "Use 1024 through 67108864.");
            _Form.Add("", _Enabled); _Form.SetFieldHint(_Form.FieldCount - 1, "Disabling prevents new runs from choosing this profile; historical snapshots remain unchanged.");
        }
        internal RunnerProfile Extract()
        {
            bool rest = _Kind.SelectedIndex == 1;
            return new RunnerProfile { Id = _Id, Name = _Name.Value, Kind = rest ? "rest" : "cli", Provider = Providers()[Math.Max(0, _Provider.SelectedIndex)], Endpoint = rest ? _Endpoint.Value : "", Model = rest ? _Model.Value : "", Executable = rest ? "" : _Executable.Value, WorkingDirectory = _Directory.Value, Arguments = rest ? new List<string>() : _Arguments.Select(field => field.Value).ToList(), SecretEnvironmentVariable = rest ? _Secret.Value : "", TimeoutSeconds = int.Parse(_Timeout.Value, CultureInfo.InvariantCulture), MaxOutputBytes = int.Parse(_Output.Value, CultureInfo.InvariantCulture), Enabled = _Enabled.Checked };
        }
    }
}
