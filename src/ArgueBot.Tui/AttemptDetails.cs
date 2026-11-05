namespace ArgueBot.Tui
{
    using ArgueBot.Core.Models;
    using System;
    using System.Globalization;
    using System.Linq;
    using TUIKit.Widgets;

    internal sealed class AttemptDetails
    {
        internal Form Form { get; } = new Form();
        internal AttemptDetails(GenerationAttempt attempt, DebateRun run)
        {
            ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(run);
            Add("Attempt", attempt.Id, "Immutable invocation identifier. Interrupted invocation does not prove external work was absent.");
            Add("State", attempt.State, "Pending, streaming, completed, interrupted or failed. A retry creates another attempt.");
            Add("Original question", run.Question, "Question frozen when this run was created. Display renames do not change historical prompts.");
            Add("Participant snapshot", attempt.ParticipantId, "Immutable runner configuration selected for this attempt.");
            if (run.ProtocolVersion == 2) { Add("Purpose", attempt.Purpose, "Question, response, synthesis, ratification or compaction."); Add("Context epoch", attempt.ContextEpoch.ToString(CultureInfo.InvariantCulture), "Votes apply only to this context epoch."); Add("Roster revision", attempt.RosterRevision.ToString(CultureInfo.InvariantCulture), "Exact panel membership version."); if (attempt.Repair) Add("Correction request", "Yes", "The previous reply was not the required structured answer, so ArgueBot asked once for a corrected reply."); }
            Add("Prompt version", attempt.PromptVersion, "Deterministic system prompt template version.");
            Add("Prompt SHA-256", attempt.PromptDigest, "Digest of the exact UTF-8 system prompt.");
            Add("Context UTF-16 characters", attempt.ContextCharacters.ToString(CultureInfo.InvariantCulture), "System prompt plus source-message content. This count is not a token estimate.");
            Add("Endpoint wait milliseconds", attempt.EndpointWaitMilliseconds.ToString(CultureInfo.InvariantCulture), "Time waiting for shared endpoint capacity, including cancelled waits. Zero for immediate admission, CLI or older records.");
            Add("Elapsed milliseconds", attempt.DurationMilliseconds.ToString(CultureInfo.InvariantCulture), "Monotonic generation and durable stream flush time. Zero if unavailable after a crash.");
            Add("Frozen timeout seconds", (run.Participants.FirstOrDefault(participant => participant.Id == attempt.ParticipantId) ?? (run.Orchestrator?.Id == attempt.ParticipantId ? run.Orchestrator : null))?.TimeoutSeconds.ToString(CultureInfo.InvariantCulture) ?? "Unavailable", "Per-turn deadline captured when the run was accepted. Resume retains this limit.");
            foreach (CredentialRevision credential in attempt.CredentialRevisions) Add("Resolved credential", credential.CredentialId + " revision " + credential.Revision, "Revision resolved atomically with its value before invocation. Secret values are never retained here.");
            Add("Safe error code", attempt.ErrorCode, "No upstream response body or credentials are recorded here.");
            if (attempt.ErrorMessage.Length > 0) Add("Why it failed / what to do", attempt.ErrorMessage, "Cause and recovery guidance for this attempt. Resume uses the frozen configuration.");
            for (int index = 0; index < attempt.ContextMessageIds.Count; index++) Add("Source message " + (index + 1).ToString(CultureInfo.InvariantCulture), attempt.ContextMessageIds[index], "Exact ordered source identifier. Other recipients and interrupted answers were excluded at acceptance.");
        }
        private void Add(string label, string value, string hint)
        { Form.Add(label, new TextField { Value = value, IsReadOnly = true }); Form.SetFieldHint(Form.FieldCount - 1, hint); }
    }
}
