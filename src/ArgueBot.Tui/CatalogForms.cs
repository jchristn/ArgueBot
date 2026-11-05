namespace ArgueBot.Tui
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TUIKit.Widgets;

    internal static class CatalogForms
    {
        private static TextField Text(Form form, string label, string value, string hint, bool required = false)
        {
            TextField field = new TextField { Value = value }; int index = form.FieldCount;
            form.Add(label, field, () => required && string.IsNullOrWhiteSpace(field.Value) ? "This value is required." : null); form.SetFieldHint(index, hint); return field;
        }
        private static Dropdown<string> Choice(Form form, string label, string[] names, int selected, string hint)
        {
            Dropdown<string> field = new Dropdown<string>(names) { SelectedIndex = Math.Max(0, selected) }; int index = form.FieldCount;
            form.Add(label, field); form.SetFieldHint(index, hint); return field;
        }
        internal static CatalogEditor<EndpointProfile> Endpoint(EndpointProfile item, List<CredentialMetadata> credentials)
        {
            CatalogEditor<EndpointProfile> editor = new CatalogEditor<EndpointProfile>(); Form form = editor.Form;
            TextField name = Text(form, "Name", item.Name, "Reusable connection name.", true);
            Dropdown<string> protocol = Choice(form, "HTTP protocol", new[] { "Ollama native chat", "OpenAI-compatible chat completions" }, item.Protocol == "openai" ? 1 : 0, "The endpoint must implement this exact protocol.");
            TextField url = Text(form, "Base URL", item.BaseUrl, "HTTP(S) URL without embedded credentials, query or fragment.", true);
            TextField timeout = Text(form, "Timeout seconds", item.TimeoutSeconds.ToString(CultureInfo.InvariantCulture), "Generation deadline, 1 through 3600 seconds.", true);
            TextField capacity = Text(form, "Concurrent generations", item.MaxConcurrentInvocations.ToString(CultureInfo.InvariantCulture), "Shared endpoint limit, 1 through 64. Queued work observes cancellation and deadlines.", true);
            Checkbox enabled = new Checkbox("Enabled for new runs", item.Enabled); form.Add("", enabled);
            List<Func<HttpAuthBinding>> bindings = new List<Func<HttpAuthBinding>>();
            string[] kinds = new[] { "bearer", "api_key", "header", "query" };
            string[] credentialNames = new[] { "None: use an environment reference or nonsecret header" }.Concat(credentials.Select(value => value.Name + " (" + value.Kind + ")")).ToArray();
            Action<HttpAuthBinding> add = binding =>
            {
                int index = bindings.Count; form.AddSection("HTTP binding " + (index + 1));
                Checkbox keep = new Checkbox("Keep this binding", true); form.Add("", keep);
                Dropdown<string> kind = Choice(form, "Binding type", kinds, Array.IndexOf(kinds, binding.Kind), "No bindings means no added authentication. Bearer uses Authorization.");
                TextField key = Text(form, "Header or query name", binding.Name, "API key, header and query bindings require a unique name.");
                Dropdown<string> credential = Choice(form, "Stored secret", credentialNames, credentials.FindIndex(value => value.Id == binding.CredentialId) + 1, "Select exactly one value source for each binding.");
                TextField environment = Text(form, "Environment reference", binding.EnvironmentVariable, "Name on the service host, never the secret value.");
                TextField value = Text(form, "Nonsecret header value", binding.Value, "Routing metadata only. Authentication values must reference a credential or environment variable.");
                bindings.Add(() => keep.Checked ? new HttpAuthBinding { Kind = kinds[kind.SelectedIndex], Name = key.Value, CredentialId = credential.SelectedIndex == 0 ? "" : credentials[credential.SelectedIndex - 1].Id, EnvironmentVariable = environment.Value, Value = value.Value } : new HttpAuthBinding { Kind = "removed" });
            };
            foreach (HttpAuthBinding binding in item.AuthBindings) add(binding);
            form.Add("", new Button("Add HTTP binding", () => { if (bindings.Count < 32) { add(new HttpAuthBinding { Kind = "bearer" }); form.SetFocusedField(form.FieldCount - 6); } }));
            editor.Extract = () => new EndpointProfile { Id = item.Id, Revision = item.Revision, Name = name.Value, Protocol = protocol.SelectedIndex == 0 ? "ollama" : "openai", BaseUrl = url.Value, TimeoutSeconds = int.Parse(timeout.Value, CultureInfo.InvariantCulture), MaxConcurrentInvocations = int.Parse(capacity.Value, CultureInfo.InvariantCulture), Enabled = enabled.Checked, AuthBindings = bindings.Select(extract => extract()).Where(binding => binding.Kind != "removed").ToList() };
            return editor;
        }
        internal static CatalogEditor<HarnessProfile> Harness(HarnessProfile item, List<CredentialMetadata> credentials)
        {
            CatalogEditor<HarnessProfile> editor = new CatalogEditor<HarnessProfile>(); Form form = editor.Form;
            TextField name = Text(form, "Name", item.Name, "Reusable harness display name.", true);
            string[] providers = new[] { "plain", "claude", "codex", "gemini", "mux" };
            Dropdown<string> provider = Choice(form, "Protocol", providers, Array.IndexOf(providers, item.Provider), "Choose a harness before adjusting its executable and arguments.");
            TextField executable = Text(form, "Executable", item.Executable, "Direct program name or path on the service host.", true);
            TextField directory = Text(form, "Working directory", item.WorkingDirectory, "Optional directory on the service host.");
            Dropdown<string> credential = Choice(form, "CLI secret", new[] { "Use existing service-host login" }.Concat(credentials.Select(value => value.Name + " (" + value.Kind + ")")).ToArray(), credentials.FindIndex(value => value.Id == item.CredentialId) + 1, "Claude OAuth token or Codex auth.json. The service injects it for each invocation. In Docker there is no host login, so choose a stored credential.");
            Checkbox enabled = new Checkbox("Enabled for new runs", item.Enabled); form.Add("", enabled);
            TextField environment = Text(form, "Inherited environment names", string.Join(", ", item.EnvironmentVariables), "Comma-separated service variable names only. Standard paths, locale, proxy and TLS settings are retained. Other variables require an explicit name here.");
            List<Func<string?>> arguments = new List<Func<string?>>();
            Action<string> add = argument =>
            {
                Checkbox keep = new Checkbox("Keep argument " + (arguments.Count + 1), true); form.Add("", keep);
                TextField value = Text(form, "Argument " + (arguments.Count + 1), argument, "One literal argument per row. Uncheck Keep to remove; an empty value remains an empty argument.");
                arguments.Add(() => keep.Checked ? value.Value : null);
            };
            foreach (string argument in item.Arguments) add(argument);
            form.Add("", new Button("Add argument", () => { if (arguments.Count < 128) add(""); }));
            editor.Extract = () => new HarnessProfile { Id = item.Id, Revision = item.Revision, Name = name.Value, Provider = providers[provider.SelectedIndex], Executable = executable.Value, WorkingDirectory = directory.Value, CredentialId = credential.SelectedIndex == 0 ? "" : credentials[credential.SelectedIndex - 1].Id, EnvironmentVariables = environment.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), Arguments = arguments.Select(extract => extract()).Where(value => value != null).Select(value => value!).ToList(), Enabled = enabled.Checked };
            return editor;
        }
        internal static CatalogEditor<ModelProfile> Model(ModelProfile item, List<EndpointProfile> endpoints, List<HarnessProfile> harnesses)
        {
            CatalogEditor<ModelProfile> editor = new CatalogEditor<ModelProfile>(); Form form = editor.Form;
            TextField name = Text(form, "Name", item.Name, "Reusable model name.", true);
            Dropdown<string> endpoint = Choice(form, "HTTP endpoint", new[] { "None" }.Concat(endpoints.Select(value => value.Name)).ToArray(), endpoints.FindIndex(value => value.Id == item.EndpointId) + 1, "Choose one endpoint or one harness.");
            Dropdown<string> harness = Choice(form, "CLI harness", new[] { "None" }.Concat(harnesses.Select(value => value.Name)).ToArray(), harnesses.FindIndex(value => value.Id == item.HarnessId) + 1, "Choose one endpoint or one harness.");
            endpoint.SelectionChanged += (_, _) => { if (endpoint.SelectedIndex > 0) harness.SelectedIndex = 0; };
            harness.SelectionChanged += (_, _) => { if (harness.SelectedIndex > 0) endpoint.SelectedIndex = 0; };
            TextField model = Text(form, "Provider model ID", item.Model, "Required for HTTP; CLI uses --model for Claude, Codex and Gemini, or a {model} argument placeholder.");
            TextField budget = Text(form, "Context character budget", item.ContextMaxCharacters.ToString(CultureInfo.InvariantCulture), "8192 through 16777216; the service applies its own limit too.", true);
            TextField output = Text(form, "Captured output bytes", item.MaxOutputBytes.ToString(CultureInfo.InvariantCulture), "1024 through 67108864 bytes. Independent of the provider's token limit.", true);
            TextField tokens = Text(form, "HTTP output tokens", item.Generation.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "", "Optional: 1 through 1048576. Blank retains the HTTP adapter default of 4096. Leave blank for CLI.");
            TextField temperature = Text(form, "HTTP temperature", item.Generation.Temperature?.ToString("R", CultureInfo.InvariantCulture) ?? "", "Optional: 0 through 2. Blank uses provider defaults. Leave blank for CLI; its flags belong in the harness.");
            TextField topP = Text(form, "HTTP top-p", item.Generation.TopP?.ToString("R", CultureInfo.InvariantCulture) ?? "", "Optional: 0 through 1. Some models reject sampling settings; verify with an explicit model check.");
            Checkbox tools = new Checkbox("Model supports tools", item.SupportsTools); form.Add("", tools);
            Checkbox enabled = new Checkbox("Enabled for new runs", item.Enabled); form.Add("", enabled);
            editor.Extract = () => new ModelProfile { Id = item.Id, Revision = item.Revision, Name = name.Value, EndpointId = endpoint.SelectedIndex == 0 ? "" : endpoints[endpoint.SelectedIndex - 1].Id, HarnessId = harness.SelectedIndex == 0 ? "" : harnesses[harness.SelectedIndex - 1].Id, Model = model.Value, ContextMaxCharacters = int.Parse(budget.Value, CultureInfo.InvariantCulture), MaxOutputBytes = int.Parse(output.Value, CultureInfo.InvariantCulture), Generation = new ModelGenerationSettings { MaxOutputTokens = string.IsNullOrWhiteSpace(tokens.Value) ? null : int.Parse(tokens.Value, CultureInfo.InvariantCulture), Temperature = string.IsNullOrWhiteSpace(temperature.Value) ? null : double.Parse(temperature.Value, CultureInfo.InvariantCulture), TopP = string.IsNullOrWhiteSpace(topP.Value) ? null : double.Parse(topP.Value, CultureInfo.InvariantCulture) }, SupportsTools = tools.Checked, Enabled = enabled.Checked };
            return editor;
        }
        /// <summary>The dedicated Codex login command for the shell of the machine running the TUI.</summary>
        internal static string CodexLoginCommand
        {
            get
            {
                return OperatingSystem.IsWindows()
                    ? "(PowerShell) New-Item -ItemType Directory -Force \"$HOME\\.codex-arguebot\" | Out-Null; $env:CODEX_HOME = \"$HOME\\.codex-arguebot\"; codex login"
                    : "mkdir -p ~/.codex-arguebot && CODEX_HOME=~/.codex-arguebot codex login";
            }
        }
        /// <summary>Edit a workspace prompt's name and description, and optionally replace its text from a local file.</summary>
        internal static CatalogEditor<PromptTemplate> Prompt(PromptTemplate item)
        {
            CatalogEditor<PromptTemplate> editor = new CatalogEditor<PromptTemplate>(); Form form = editor.Form;
            TextField name = Text(form, "Prompt name", item.Name, "Shown when choosing prompts for a conversation.", true);
            TextField description = Text(form, "Description", item.Description, "Optional note about when to use this prompt.");
            TextField file = Text(form, "Replace text from file (optional)", "", "Path to a UTF-8 text file with the new prompt text. Leave blank to keep the current text. Use {parameters} such as {question}; see prompts parameters.");
            editor.Extract = () =>
            {
                string body = item.Body;
                if (file.Value.Length > 0)
                {
                    System.IO.FileInfo info = new System.IO.FileInfo(file.Value);
                    if (info.Length > 256 * 1024) throw new InvalidOperationException("Prompt files must be at most 256 KiB.");
                    body = System.IO.File.ReadAllText(file.Value);
                }
                return new PromptTemplate { Id = item.Id, Name = name.Value, Description = description.Value, Purpose = item.Purpose, Body = body, Revision = item.Revision };
            };
            return editor;
        }
        internal static CatalogEditor<CredentialRequest> Credential(CredentialMetadata? item)
        {
            CatalogEditor<CredentialRequest> editor = new CatalogEditor<CredentialRequest>(); Form form = editor.Form;
            TextField name = Text(form, "Safe display name", item?.Name ?? "", "Never include a secret in the display name.", true);
            string[] kinds = new[] { "claude_oauth", "codex_auth_json", "http_bearer", "http_header", "http_query", "token" };
            string[] kindLabels = new[] { "Claude Code OAuth token", "Codex auth.json login", "Bearer token", "Header key and value", "Query string key and value", "HTTP token (placement set on the endpoint)" };
            Dropdown<string> kind = Choice(form, "Secret kind", kindLabels, Math.Max(0, Array.IndexOf(kinds, item?.Kind ?? "claude_oauth")), "Claude Code: run claude setup-token and paste the token. Codex: run " + CodexLoginCommand + ", then give the path to its auth.json. Bearer, header or query: an API key from the provider console; header and query kinds also need the parameter name below. Bind it to an endpoint. Existing kinds cannot change.");
            TextField parameter = Text(form, "Header or query name", item?.ParameterName ?? "", "Only for header and query kinds, for example x-api-key or key. Fixed after creation.");
            TextField secret = Text(form, "Paste value (masked)", "", "Write-only. Paste a token or complete Codex auth.json. Use a dedicated CLI login that no other process refreshes."); secret.MaskChar = '*';
            TextField file = Text(form, "Or secret file path", "", "Optional local file. Read locally and send to the service; never persisted as configuration.");
            editor.Extract = () =>
            {
                string value = secret.Value;
                if (file.Value.Length > 0)
                {
                    System.IO.FileInfo info = new System.IO.FileInfo(file.Value);
                    if (info.Length > 65536) throw new InvalidOperationException("Secret files must be at most 64 KiB.");
                    value = System.IO.File.ReadAllText(file.Value);
                }
                string chosen = kinds[kind.SelectedIndex];
                return new CredentialRequest { Name = name.Value, Kind = chosen, ParameterName = chosen == "http_header" || chosen == "http_query" ? parameter.Value.Trim() : "", SecretValue = value, Revision = item?.Revision ?? 0 };
            };
            return editor;
        }
    }
}
