namespace ArgueBot.Tui
{
    using System;
    using ArgueBot.Core.Models;

    internal static class AgentPresets
    {
        internal static string[] Choices { get { return new[] { "Claude Code (installed CLI)", "Codex (installed CLI)", "Gemini (installed CLI)", "Mux (installed CLI)", "REST model endpoint (Ollama / OpenAI / Anthropic / Gemini)", "Custom CLI harness" }; } }
        internal static RunnerProfile Create(int index)
        {
            switch (index)
            {
                case 0: return HarnessDefaults.Create("claude");
                case 1: return HarnessDefaults.Create("codex");
                case 2: return HarnessDefaults.Create("gemini");
                case 3: return HarnessDefaults.Create("mux");
                case 4: return new RunnerProfile { Name = "My model", Kind = "rest", Provider = "ollama", Endpoint = "http://127.0.0.1:11434" };
                case 5: return new RunnerProfile { Name = "My agent", Kind = "cli", Provider = "plain" };
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
