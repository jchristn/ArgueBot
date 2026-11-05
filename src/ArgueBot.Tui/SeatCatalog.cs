namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Debate seats for the terminal: the saved agents, each described by how it runs and signs in. The returned profiles carry
    /// only the identifier, name, enabled state and that description (in Kind).
    /// </summary>
    internal static class SeatCatalog
    {
        internal static async Task<List<RunnerProfile>> LoadAsync(ArgueBotClient client, CancellationToken token)
        {
            Task<List<ModelProfile>> models = client.ModelsAsync(token);
            Task<List<CredentialMetadata>> secrets = client.CredentialsAsync(token);
            Task<List<RunnerProfile>> agents = client.ParticipantsAsync(token);
            await Task.WhenAll(models, secrets, agents).ConfigureAwait(false);
            List<RunnerProfile> seats = new List<RunnerProfile>();
            foreach (RunnerProfile agent in agents.Result)
            {
                ModelProfile? model = models.Result.FirstOrDefault(item => item.Id == agent.ModelProfileId);
                string detail;
                if (agent.ModelProfileId.Length > 0) detail = model == null ? "its model is missing" : "model " + model.Name;
                else if (agent.Kind == "rest") detail = "HTTP " + agent.Provider + (agent.Model.Length > 0 ? " " + agent.Model : "");
                else
                {
                    string program = HarnessDefaults.Create(agent.Provider).Name;
                    string signIn = agent.CredentialId.Length == 0 ? "service machine login" : "secret " + (secrets.Result.FirstOrDefault(item => item.Id == agent.CredentialId)?.Name ?? agent.CredentialId);
                    detail = (program.Length > 0 ? program : agent.Executable) + (agent.Model.Length > 0 ? ", " + agent.Model : "") + ", " + signIn;
                }
                seats.Add(new RunnerProfile { Id = agent.Id, Name = agent.Name, Enabled = agent.Enabled && (model?.Enabled ?? true), Kind = detail });
            }
            return seats;
        }
    }
}
