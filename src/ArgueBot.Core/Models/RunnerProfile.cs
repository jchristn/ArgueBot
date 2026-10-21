namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>RunnerProfile transport and domain data.</summary>
    public sealed class RunnerProfile
    {
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

        /// <summary>REST model identifier.</summary>
        public string Model { get { return _Model; } set { ArgumentNullException.ThrowIfNull(value); _Model = value; } }

        /// <summary>Direct executable path or name.</summary>
        public string Executable { get { return _Executable; } set { ArgumentNullException.ThrowIfNull(value); _Executable = value; } }

        /// <summary>Ordered arguments; prompts use standard input.</summary>
        public List<string> Arguments { get { return _Arguments; } set { ArgumentNullException.ThrowIfNull(value); _Arguments = value; } }

        /// <summary>Directory on the execution host for CLI harnesses and REST local tools; empty uses the service directory.</summary>
        public string WorkingDirectory { get { return _WorkingDirectory; } set { ArgumentNullException.ThrowIfNull(value); _WorkingDirectory = value; } }

        /// <summary>Environment reference to credential; never its value.</summary>
        public string SecretEnvironmentVariable { get { return _SecretEnvironmentVariable; } set { ArgumentNullException.ThrowIfNull(value); _SecretEnvironmentVariable = value; } }

        /// <summary>Maximum generation seconds.</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Maximum captured output bytes.</summary>
        public int MaxOutputBytes { get; set; } = 1048576;

        /// <summary>Whether new work can use this profile.</summary>
        public bool Enabled { get; set; } = true;

    }
}
