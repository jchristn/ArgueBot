namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>HarnessProfile configuration contract.</summary>
    public sealed class HarnessProfile
    {
        private string _Id = "";
        /// <summary>Stable harness ID.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Provider = "plain";
        /// <summary>CLI output protocol.</summary>
        public string Provider { get { return _Provider; } set { System.ArgumentNullException.ThrowIfNull(value); _Provider = value; } }

        private string _Executable = "";
        /// <summary>Direct executable or path on the execution host.</summary>
        public string Executable { get { return _Executable; } set { System.ArgumentNullException.ThrowIfNull(value); _Executable = value; } }

        private string _WorkingDirectory = "";
        /// <summary>Execution directory on the service host.</summary>
        public string WorkingDirectory { get { return _WorkingDirectory; } set { System.ArgumentNullException.ThrowIfNull(value); _WorkingDirectory = value; } }

        private string _CredentialId = "";
        /// <summary>Claude OAuth token or Codex auth.json credential reference.</summary>
        public string CredentialId { get { return _CredentialId; } set { System.ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }

        /// <summary>Whether new runs may use this harness.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; } = 0;

        private List<string> _Arguments = new List<string>();
        /// <summary>Literal ordered argument vector; prompts use standard input.</summary>
        public List<string> Arguments { get { return _Arguments; } set { System.ArgumentNullException.ThrowIfNull(value); _Arguments = value; } }

        private List<string> _EnvironmentVariables = new List<string>();
        /// <summary>Additional service environment variable names to inherit. Values are never stored or returned.</summary>
        public List<string> EnvironmentVariables { get { return _EnvironmentVariables; } set { System.ArgumentNullException.ThrowIfNull(value); _EnvironmentVariables = value; } }

    }
}
