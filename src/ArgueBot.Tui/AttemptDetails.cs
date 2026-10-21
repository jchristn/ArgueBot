namespace ArgueBot.Tui
{
    using System;
    using System.Globalization;
    using ArgueBot.Core.Models;
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
            Add("Prompt version", attempt.PromptVersion, "Deterministic system prompt template version.");
            Add("Prompt SHA-256", attempt.PromptDigest, "Digest of the exact UTF-8 system prompt.");
            Add("Context UTF-16 characters", attempt.ContextCharacters.ToString(CultureInfo.InvariantCulture), "System prompt plus source-message content. This count is not a token estimate.");
            Add("Elapsed milliseconds", attempt.DurationMilliseconds.ToString(CultureInfo.InvariantCulture), "Monotonic generation and durable stream flush time. Zero if unavailable after a crash.");
            Add("Safe error code", attempt.ErrorCode, "No upstream response body or credentials are recorded here.");
            if (attempt.ErrorCode == "Win32Exception") Add("What happened", "The agent program could not start. In Agents, check the executable and working directory. Enter only a program name or path, with flags in separate Argument rows. Then start a new debate; Resume keeps the original settings.", "A process launch error occurred before the agent could answer.");
            for (int index = 0; index < attempt.ContextMessageIds.Count; index++) Add("Source message " + (index + 1).ToString(CultureInfo.InvariantCulture), attempt.ContextMessageIds[index], "Exact ordered source identifier. Other recipients and interrupted answers were excluded at acceptance.");
        }
        private void Add(string label, string value, string hint)
        { Form.Add(label, new TextField { Value = value, IsReadOnly = true }); Form.SetFieldHint(Form.FieldCount - 1, hint); }
    }
}
