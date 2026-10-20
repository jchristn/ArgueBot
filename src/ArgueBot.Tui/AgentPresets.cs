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
                case 0: return new RunnerProfile { Name = "Claude", Kind = "cli", Provider = "claude", Executable = "claude" };
                case 1: return new RunnerProfile { Name = "Codex", Kind = "cli", Provider = "codex", Executable = "codex" };
                case 2: return new RunnerProfile { Name = "Gemini", Kind = "cli", Provider = "gemini", Executable = "gemini" };
                case 3: return new RunnerProfile { Name = "Mux", Kind = "cli", Provider = "mux", Executable = "mux" };
                case 4: return new RunnerProfile { Name = "My model", Kind = "rest", Provider = "ollama", Endpoint = "http://127.0.0.1:11434" };
                case 5: return new RunnerProfile { Name = "My agent", Kind = "cli", Provider = "plain" };
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
