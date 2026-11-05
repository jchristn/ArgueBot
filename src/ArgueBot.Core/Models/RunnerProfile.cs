namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>RunnerProfile transport and domain data.</summary>
    public sealed class RunnerProfile
    {
        private string _ModelProfileId = "";
        /// <summary>Reusable model reference. Empty retains inline legacy configuration.</summary>
        public string ModelProfileId { get { return _ModelProfileId; } set { System.ArgumentNullException.ThrowIfNull(value); _ModelProfileId = value; } }
        private string _RoleInstructions = "";
        /// <summary>Role-specific participant instructions.</summary>
        public string RoleInstructions { get { return _RoleInstructions; } set { System.ArgumentNullException.ThrowIfNull(value); _RoleInstructions = value; } }
        private List<HttpAuthBinding> _AuthBindings = new List<HttpAuthBinding>();
        /// <summary>Frozen HTTP bindings containing only nonsecret values and credential references.</summary>
        public List<HttpAuthBinding> AuthBindings { get { return _AuthBindings; } set { System.ArgumentNullException.ThrowIfNull(value); _AuthBindings = value; } }
        private string _CredentialId = "";
        /// <summary>Optional stored secret that signs in a CLI agent: a Claude Code token for claude or a Codex login for codex. Empty uses the service machine's own login.</summary>
        public string CredentialId { get { return _CredentialId; } set { System.ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }
        /// <summary>Exact model context character budget.</summary>
        public int ContextMaxCharacters { get; set; } = 262144;
        /// <summary>Declared tool support.</summary>
        public bool SupportsTools { get; set; } = true;
        private ModelGenerationSettings _Generation = new ModelGenerationSettings();
        /// <summary>HTTP generation settings frozen with this participant snapshot.</summary>
        public ModelGenerationSettings Generation { get { return _Generation; } set { ArgumentNullException.ThrowIfNull(value); _Generation = value; } }
        /// <summary>Model revision frozen at admission.</summary>
        public long ModelRevision { get; set; }
        private string _TransportId = "";
        /// <summary>Endpoint identity frozen at admission; endpoint capacity remains a live resource limit.</summary>
        public string TransportId { get { return _TransportId; } set { ArgumentNullException.ThrowIfNull(value); _TransportId = value; } }
        /// <summary>Endpoint revision frozen at admission.</summary>
        public long TransportRevision { get; set; }
        /// <summary>Ephemeral service-only injection. Excluded from serialization and persistence.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, string> RuntimeEnvironment { get; } = new Dictionary<string, string>();
        /// <summary>Restrict a CLI agent to standard process variables plus the named environmentVariables; false passes the service environment.</summary>
        public bool RestrictEnvironment { get; set; }
        private List<string> _EnvironmentVariables = new List<string>();
        /// <summary>Additional inherited variable names frozen at admission. Values are resolved only when the child starts.</summary>
        public List<string> EnvironmentVariables { get { return _EnvironmentVariables; } set { ArgumentNullException.ThrowIfNull(value); _EnvironmentVariables = value; } }
        /// <summary>Ephemeral resolved HTTP values. Excluded from serialization and persistence.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, string> RuntimeCredentials { get; } = new Dictionary<string, string>();
        /// <summary>Service-only callback to persist resolved credential revisions before invoking a provider.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<CredentialMetadata, System.Threading.CancellationToken, System.Threading.Tasks.Task>? RuntimeCredentialResolved { get; set; }
        /// <summary>Service-only callback recording endpoint wait time, including cancellation while queued.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Func<long, System.Threading.CancellationToken, System.Threading.Tasks.Task>? RuntimeEndpointWait { get; set; }
        /// <summary>Tenant whose endpoints and secrets this invocation may use. Set by the service for each invocation; never persisted.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string RuntimeTenantId { get { return _RuntimeTenantId; } set { ArgumentNullException.ThrowIfNull(value); _RuntimeTenantId = value; } }
        private string _RuntimeTenantId = "";
        /// <summary>Invocation-only MCP bridge process. Never persisted or accepted from a transport document.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<string> RuntimeMcpCommand { get; } = new List<string>();
        private string _Id = "";
        private string _Name = "";
        private string _Kind = "cli";
        private string _Provider = "plain";
        private string _Endpoint = "";
        private string _Model = "";
        private string _Executable = "";
        private List<string> _Arguments = new List<string>();
        private string _WorkingDirectory = "";
        private string _SecretEnvironmentVariable = "";

        /// <summary>Stable profile identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Participant display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Runner kind: cli or rest.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        /// <summary>Protocol adapter.</summary>
        public string Provider { get { return _Provider; } set { ArgumentNullException.ThrowIfNull(value); _Provider = value; } }

        /// <summary>REST base URI.</summary>
        public string Endpoint { get { return _Endpoint; } set { ArgumentNullException.ThrowIfNull(value); _Endpoint = value; } }

        /// <summary>Model identifier: required for HTTP agents; optional for CLI agents, passed as --model to Claude Code, Codex and Gemini or into a literal {model} argument.</summary>
        public string Model { get { return _Model; } set { ArgumentNullException.ThrowIfNull(value); _Model = value; } }

        /// <summary>Direct executable path or name.</summary>
        public string Executable { get { return _Executable; } set { ArgumentNullException.ThrowIfNull(value); _Executable = value; } }

        /// <summary>Ordered arguments; prompts use standard input.</summary>
        public List<string> Arguments { get { return _Arguments; } set { ArgumentNullException.ThrowIfNull(value); _Arguments = value; } }

        /// <summary>Directory on the execution host for CLI agents and REST local tools; empty uses the service directory.</summary>
        public string WorkingDirectory { get { return _WorkingDirectory; } set { ArgumentNullException.ThrowIfNull(value); _WorkingDirectory = value; } }

        /// <summary>Environment reference to credential; never its value.</summary>
        public string SecretEnvironmentVariable { get { return _SecretEnvironmentVariable; } set { ArgumentNullException.ThrowIfNull(value); _SecretEnvironmentVariable = value; } }

        /// <summary>Maximum generation seconds.</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Maximum captured output bytes; defaults to 64 MiB.</summary>
        public int MaxOutputBytes { get; set; } = 67108864;

        /// <summary>Whether new work can use this profile.</summary>
        public bool Enabled { get; set; } = true;

    }
}
