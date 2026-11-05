namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Passes a CLI agent's optional model name to its program.</summary>
    public static class CliModelArguments
    {
        /// <summary>True when the agent's program can receive a model name: a named CLI, or a literal <c>{model}</c> argument.</summary>
        public static bool CanSelect(RunnerProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return Named(profile.Provider) || profile.Arguments.Contains("{model}");
        }

        /// <summary>Return the arguments to run with: the model is appended as <c>--model</c> for named CLIs, or replaces each <c>{model}</c> argument.</summary>
        public static List<string> Apply(RunnerProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            List<string> result = new List<string>(profile.Arguments);
            if (profile.Kind != "cli") return result;
            if (result.Contains("{model}")) return result.Select(value => value == "{model}" ? profile.Model : value).ToList();
            if (profile.Model.Length == 0 || !Named(profile.Provider) || result.Contains("--model") || result.Contains("-m")) return result;
            result.Add("--model"); result.Add(profile.Model);
            return result;
        }

        private static bool Named(string provider) { return provider == "claude" || provider == "codex" || provider == "gemini"; }
    }
}
