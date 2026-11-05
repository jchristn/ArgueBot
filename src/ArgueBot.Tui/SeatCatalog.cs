namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Debate seats for the terminal: reusable models and CLI harnesses, which carry their secrets, then saved agents.
    /// A directly configured CLI agent has no stored secret and relies on the service machine's login; its Kind says so.
    /// </summary>
    internal static class SeatCatalog
    {
        internal static async Task<List<RunnerProfile>> LoadAsync(ArgueBotClient client, CancellationToken token)
        {
            Task<List<ModelProfile>> models = client.ModelsAsync(token);
            Task<List<HarnessProfile>> harnesses = client.HarnessesAsync(token);
            Task<List<EndpointProfile>> endpoints = client.EndpointsAsync(token);
            Task<List<CredentialMetadata>> secrets = client.CredentialsAsync(token);
            Task<List<RunnerProfile>> agents = client.ParticipantsAsync(token);
            await Task.WhenAll(models, harnesses, endpoints, secrets, agents).ConfigureAwait(false);
            string SignIn(HarnessProfile harness) => harness.CredentialId.Length == 0 ? "no secret: uses the service machine's login" : "signed in with " + (secrets.Result.FirstOrDefault(item => item.Id == harness.CredentialId)?.Name ?? harness.CredentialId);
            string Describe(ModelProfile model)
            {
                if (model.HarnessId.Length > 0)
                {
                    HarnessProfile? harness = harnesses.Result.FirstOrDefault(item => item.Id == model.HarnessId);
                    return "model, " + (harness == null ? "its CLI harness is missing" : harness.Name + ", " + SignIn(harness));
                }
                EndpointProfile? endpoint = endpoints.Result.FirstOrDefault(item => item.Id == model.EndpointId);
                return "model, " + (endpoint == null ? "its endpoint is missing" : endpoint.Name + (endpoint.AuthBindings.Count > 0 ? ", endpoint authentication" : ", no authentication"));
            }
            List<RunnerProfile> seats = new List<RunnerProfile>();
            foreach (ModelProfile model in models.Result) seats.Add(new RunnerProfile { Id = model.Id, Name = model.Name, Enabled = model.Enabled, Kind = Describe(model) });
            foreach (HarnessProfile harness in harnesses.Result) seats.Add(new RunnerProfile { Id = harness.Id, Name = harness.Name, Enabled = harness.Enabled, Kind = "CLI harness, " + SignIn(harness) });
            foreach (RunnerProfile agent in agents.Result)
            {
                ModelProfile? model = models.Result.FirstOrDefault(item => item.Id == agent.ModelProfileId);
                string detail = model != null ? Describe(model) : agent.ModelProfileId.Length > 0 ? "its model is missing" : agent.Kind == "rest" ? "REST " + agent.Provider : "CLI configured directly, no secret: uses the service machine's login";
                seats.Add(new RunnerProfile { Id = agent.Id, Name = agent.Name, Enabled = agent.Enabled && (model?.Enabled ?? true), Kind = "agent, " + detail });
            }
            return seats;
        }
    }
}
