namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Turns a debate seat selection into a runner profile. A seat can be a reusable model (<c>mod_</c>), which carries its
    /// harness or endpoint and their secrets; a CLI harness (<c>har_</c>), which carries its secret; or a saved agent profile.
    /// </summary>
    internal static class SeatSelection
    {
        internal static async Task<RunnerProfile> ReadAsync(DatabaseDriverBase database, RequestContext context, string id, string field, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(id);
            RunnerProfile profile;
            if (id.StartsWith("mod_", StringComparison.Ordinal))
            {
                ModelProfile model = (await database.Catalog.ModelsAsync(context, token, id).ConfigureAwait(false)).FirstOrDefault() ?? throw new DomainException(404, "profile_not_found", "A selected model was deleted. Choose another.", field);
                profile = new RunnerProfile { Name = model.Name, ModelProfileId = model.Id, Enabled = model.Enabled };
            }
            else if (id.StartsWith("har_", StringComparison.Ordinal))
            {
                HarnessProfile harness = (await database.Catalog.HarnessesAsync(context, token, id).ConfigureAwait(false)).FirstOrDefault() ?? throw new DomainException(404, "profile_not_found", "A selected CLI harness was deleted. Choose another.", field);
                profile = new RunnerProfile
                {
                    Name = harness.Name, Enabled = harness.Enabled, Kind = "cli", Provider = harness.Provider, Executable = harness.Executable,
                    Arguments = new List<string>(harness.Arguments), WorkingDirectory = harness.WorkingDirectory, CredentialId = harness.CredentialId,
                    RestrictEnvironment = true, EnvironmentVariables = new List<string>(harness.EnvironmentVariables), TransportId = harness.Id, TransportRevision = harness.Revision
                };
                return profile;
            }
            else
            {
                try { profile = await database.Profiles.ReadAsync(context, id, token).ConfigureAwait(false); }
                catch (DomainException error) when (error.Code == "not_found") { throw new DomainException(404, "profile_not_found", "A selected profile was deleted. Choose another agent.", field); }
            }
            try { await database.Catalog.ResolveAsync(context, profile, token).ConfigureAwait(false); }
            catch (DomainException error) when (error.Field.Length == 0) { throw new DomainException(error.StatusCode, error.Code, profile.Name + ": " + error.Message, field); }
            return profile;
        }
    }
}
