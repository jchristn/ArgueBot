namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class FailureExplanationChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string[] details = { "Agent stdout exceeded the configured capture limit.", "Agent stderr exceeded the configured capture limit.", "Provider answer exceeded the configured capture limit." };
            string[] codes = { "agent_stdout_limit", "agent_stderr_limit", "provider_output_limit" };
            for (int index = 0; index < details.Length; index++)
            {
                string code = GenerationFailure.Code(new IOException(details[index]));
                if (code != codes[index] || !GenerationFailure.Explain(code).Contains("Resume keeps the original limit", StringComparison.Ordinal)) throw new InvalidDataException("I/O overflow did not explain its cause and frozen-limit recovery.");
            }
            if (GenerationFailure.Code(new IOException("Fictional private payload")) != "IOException" || GenerationFailure.Explain("IOException").Contains("Fictional private payload", StringComparison.Ordinal)) throw new InvalidDataException("Unknown I/O payload entered a public explanation.");
            string home = Path.Combine(Path.GetTempPath(), "arguebot-failure-explanation-" + Guid.NewGuid().ToString("N"));
            try
            {
                string directory = Path.Combine(home, "logs", "runner-failures"); Directory.CreateDirectory(directory);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                await using (ConversationEngine engine = new ConversationEngine(database, new CliRunner(), diagnosticDirectory: directory))
                {
                    GenerationAttempt attempt = new GenerationAttempt { Id = "att_fixture", ConversationId = "cnv_fixture", RunId = "run_fixture", ErrorCode = "IOException", State = "failed" };
                    string path = Path.Combine(directory, attempt.Id + ".json");
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { attempt.Id, attempt.ConversationId, attempt.RunId, Diagnostics = new { Detail = details[0], StandardOutput = "Fictional private payload" } }), token).ConfigureAwait(false);
                    await engine.DescribeFailureAsync(attempt, token).ConfigureAwait(false);
                    if (attempt.ErrorCode != "IOException" || !attempt.ErrorMessage.Contains("tool results", StringComparison.Ordinal) || attempt.ErrorMessage.Contains("Fictional private payload", StringComparison.Ordinal)) throw new InvalidDataException("Historical failure lost its exact safe cause or leaked raw output.");
                    foreach (string invalid in new[] { "{}", "{invalid", "[]", JsonSerializer.Serialize(new { Id = "att_other", attempt.ConversationId, attempt.RunId, Diagnostics = new { Detail = details[0] } }) })
                    {
                        await File.WriteAllTextAsync(path, invalid, token).ConfigureAwait(false);
                        await engine.DescribeFailureAsync(attempt, token).ConfigureAwait(false);
                        if (!attempt.ErrorMessage.Contains("exact cause was not recorded", StringComparison.Ordinal)) throw new InvalidDataException("Unavailable or mismatched diagnostics invented a cause.");
                    }
                    File.Delete(path);
                    await engine.DescribeFailureAsync(attempt, token).ConfigureAwait(false);
                    if (!attempt.ErrorMessage.Contains("exact cause was not recorded", StringComparison.Ordinal)) throw new InvalidDataException("Missing diagnostics hid historical I/O recovery guidance.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
