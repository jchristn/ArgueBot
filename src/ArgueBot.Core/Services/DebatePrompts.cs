namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;

    internal static class DebatePrompts
    {
        internal static RunnerProfile Profile(DebateRun run)
        {
            if (run.Phase == "question" || run.Phase == "synthesis" || run.Phase == "compaction") return run.Orchestrator ?? throw new DomainException(409, "missing_orchestrator", "Select an orchestrator before starting this debate.");
            return run.Participants[(run.SpeakerIndex + run.Round - 1) % run.Participants.Count];
        }
        internal static string Build(DebateRun run, RunnerProfile profile)
        {
            string prompt = BuildPhase(run, profile);
            if (run.RepairPending && run.Phase == "compaction") prompt += "\nYour previous summary was rejected by validation. Reply again with only the JSON object: every supplied source ID exactly once, all seven sections, each pinned constraint's source ID named in the constraints section, and the size limits. This is the only correction request; another invalid summary pauses the debate and keeps the previous memory.";
            else if (run.RepairPending && run.Phase != "response") prompt += "\nYour previous reply for this step was rejected because it was not the required JSON object or named the wrong proposal. Reply again with only that JSON object and no other text. This is the only correction request; another invalid reply pauses the debate for the moderator.";
            return prompt;
        }
        private static string BuildPhase(DebateRun run, RunnerProfile profile)
        {
            string purpose = PromptCatalog.Purposes.Contains(run.Phase) ? run.Phase : "response";
            string body = run.Prompts.FirstOrDefault(item => item.Purpose == purpose)?.Body ?? PromptCatalog.FactoryFor(purpose).Body;
            return PromptCatalog.Render(body, Values(run, profile, purpose));
        }
        /// <summary>Placeholder values for one invocation. Runs started before prompts were stored use the factory text.</summary>
        internal static Dictionary<string, string> Values(DebateRun run, RunnerProfile profile, string purpose)
        {
            string guidance = purpose == "question" ? run.Policy.QuestionInstructions : purpose == "synthesis" ? run.Policy.SynthesisInstructions : purpose == "compaction" ? run.Policy.SummaryInstructions : "";
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent_name"] = profile.Name,
                ["role_instructions"] = Profile(run).RoleInstructions,
                ["question"] = run.Question,
                ["panel"] = string.Join(", ", run.Participants.Select(seat => seat.Name)),
                ["round"] = run.Round.ToString(CultureInfo.InvariantCulture),
                ["max_rounds"] = run.MaxRounds.ToString(CultureInfo.InvariantCulture),
                ["context_epoch"] = run.ContextEpoch.ToString(CultureInfo.InvariantCulture),
                ["roster_revision"] = run.RosterRevision.ToString(CultureInfo.InvariantCulture),
                ["guidance"] = guidance,
                ["current_question"] = run.CurrentQuestion,
                ["proposal"] = JsonSerializer.Serialize(run.ProposalText),
                ["proposal_id"] = JsonSerializer.Serialize(run.ProposalId),
                ["resources"] = run.ResourceCatalog,
            };
        }
    }
}
