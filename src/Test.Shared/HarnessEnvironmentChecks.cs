namespace Test.Shared
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class HarnessEnvironmentChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, RequestContext context, CancellationToken token)
        {
            RunnerProfile frozen = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Environment fixture", Kind = "cli", Provider = "plain", Executable = "dotnet", RestrictEnvironment = true, EnvironmentVariables = new List<string> { "ARGUEBOT_FIXTURE_ALLOWED" } }, token).ConfigureAwait(false);
            RunnerProfile loaded = (await database.Profiles.EnumerateAsync(context, token).ConfigureAwait(false)).Single(profile => profile.Id == frozen.Id);
            if (!loaded.RestrictEnvironment || !loaded.EnvironmentVariables.SequenceEqual(new[] { "ARGUEBOT_FIXTURE_ALLOWED" })) throw new InvalidDataException("Agent environment names did not survive storage.");
            loaded.EnvironmentVariables = new List<string> { "ARGUEBOT_FIXTURE_OTHER" };
            await database.Profiles.UpdateAsync(context, loaded.Id, loaded, token).ConfigureAwait(false);
            loaded = (await database.Profiles.EnumerateAsync(context, token).ConfigureAwait(false)).Single(profile => profile.Id == frozen.Id);
            if (!loaded.RestrictEnvironment || !loaded.EnvironmentVariables.SequenceEqual(new[] { "ARGUEBOT_FIXTURE_OTHER" })) throw new InvalidDataException("An agent environment update was not stored.");
            foreach (List<string> invalid in new[] { new List<string> { "NAME=fictional" }, new List<string> { "NAME", "name" }, Enumerable.Range(0, 65).Select(index => "FIXTURE_" + index).ToList() })
            {
                try { await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Invalid environment", Kind = "cli", Provider = "plain", Executable = "dotnet", RestrictEnvironment = true, EnvironmentVariables = invalid }, token).ConfigureAwait(false); throw new InvalidDataException("Invalid environment names were accepted."); }
                catch (DomainException failure) when (failure.Code == "invalid_configuration") { }
            }
            string? fixture = Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE");
            if (string.IsNullOrEmpty(fixture)) return; // Storage evidence is independent of the optional real-process fixture.
            string? previousAllowed = Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_ALLOWED");
            string? previousSecret = Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_SECRET");
            string? previousToken = Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_ACCESS_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_ALLOWED", "fictional-allowed");
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_SECRET", "fictional-unrelated-secret");
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_ACCESS_TOKEN", "fictional-access-token-value");
                frozen.Arguments = new List<string> { fixture, "environment" };
                StringBuilder answer = new StringBuilder();
                await new CliRunner().GenerateAsync(frozen, "Check inherited environment.", new List<Message>(), (text, _) => { answer.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
                if (answer.ToString().Trim() != "Allowed environment verified.") throw new InvalidDataException("Harness inherited an unrelated service variable or lost its selected name.");
                string temporary = Path.Combine(Path.GetTempPath(), "arguebot-environment-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, temporary, context, token).ConfigureAwait(false))
                    {
                        frozen.Arguments[1] = "environment-echo"; answer.Clear();
                        frozen.EnvironmentVariables.Add("ARGUEBOT_FIXTURE_ACCESS_TOKEN");
                        CredentialRunner runner = new CredentialRunner(true, vault, database, context, Path.Combine(temporary, "runtime"));
                        await runner.GenerateAsync(frozen, "Do not expose inherited values.", new List<Message>(), (text, _) => { answer.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
                        if (answer.ToString().Trim() != "allowed=fictional-allowed;token=[redacted]") throw new InvalidDataException("Inherited configuration must stay visible while credential-named values are redacted: " + answer.ToString().Trim());
                    }
                }
                finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            }
            finally
            {
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_ALLOWED", previousAllowed);
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_SECRET", previousSecret);
                Environment.SetEnvironmentVariable("ARGUEBOT_FIXTURE_ACCESS_TOKEN", previousToken);
            }
        }
    }
}
