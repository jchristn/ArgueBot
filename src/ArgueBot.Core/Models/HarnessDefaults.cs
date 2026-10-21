namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>Shared installed-harness defaults for editors, persistence and execution.</summary>
    public static class HarnessDefaults
    {
        private static readonly JsonDocument _Catalog = Load();

        /// <summary>Return a fresh editable CLI profile for the selected provider.</summary>
        public static RunnerProfile Create(string provider)
        {
            RunnerProfile profile = new RunnerProfile { Kind = "cli", Provider = provider };
            if (_Catalog.RootElement.TryGetProperty(provider, out JsonElement preset))
            {
                profile.Name = preset.GetProperty("name").GetString()!;
                profile.Executable = preset.GetProperty("executable").GetString()!;
                foreach (JsonElement argument in preset.GetProperty("arguments").EnumerateArray()) profile.Arguments.Add(argument.GetString()!);
            }
            return profile;
        }

        /// <summary>Fill omitted named-harness configuration without replacing explicit custom arguments.</summary>
        public static void ApplyMissing(RunnerProfile profile)
        {
            if (profile.Kind != "cli") return;
            RunnerProfile defaults = Create(profile.Provider);
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = defaults.Name;
            if (string.IsNullOrWhiteSpace(profile.Executable)) profile.Executable = defaults.Executable;
            if (profile.Arguments.Count == 0) profile.Arguments = new List<string>(defaults.Arguments);
        }

        private static JsonDocument Load()
        {
            using (Stream stream = typeof(HarnessDefaults).Assembly.GetManifestResourceStream("ArgueBot.HarnessDefaults.json")!)
            {
                return JsonDocument.Parse(stream);
            }
        }
    }
}
