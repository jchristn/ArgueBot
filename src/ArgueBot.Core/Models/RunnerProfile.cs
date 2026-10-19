namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>RunnerProfile transport and domain data.</summary>
    public sealed class RunnerProfile
    {
        /// <summary>Stable profile identifier.</summary>
        public string Id { get; set; } = "";

        /// <summary>Participant display name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Runner kind: cli or rest.</summary>
        public string Kind { get; set; } = "cli";

        /// <summary>Protocol adapter.</summary>
        public string Provider { get; set; } = "plain";

        /// <summary>REST base URI.</summary>
        public string Endpoint { get; set; } = "";

        /// <summary>REST model identifier.</summary>
        public string Model { get; set; } = "";

        /// <summary>Direct executable path or name.</summary>
        public string Executable { get; set; } = "";

        /// <summary>Ordered arguments; prompts use standard input.</summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>Directory on execution host.</summary>
        public string WorkingDirectory { get; set; } = "";

        /// <summary>Environment reference to credential; never its value.</summary>
        public string SecretEnvironmentVariable { get; set; } = "";

        /// <summary>Maximum generation seconds.</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Maximum captured output bytes.</summary>
        public int MaxOutputBytes { get; set; } = 1048576;

        /// <summary>Whether new work can use this profile.</summary>
        public bool Enabled { get; set; } = true;

    }
}
