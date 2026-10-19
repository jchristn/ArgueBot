namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Observability;
    using ArgueBot.Core.Runners;

    /// <summary>Service-owned debate execution. Client disconnects never cancel accepted work.</summary>
    public sealed class ConversationEngine : IAsyncDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly IRunner _Runner;
        private readonly ILogger _Logger;
        private readonly CancellationTokenSource _Lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _Capacity;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Gates = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly ConcurrentDictionary<string, ExecutionWorker> _Workers = new ConcurrentDictionary<string, ExecutionWorker>();

        /// <summary>Create an engine with bounded concurrent conversations, default four.</summary>
        public ConversationEngine(DatabaseDriverBase database, IRunner runner, int capacity = 4, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(database); ArgumentNullException.ThrowIfNull(runner);
            if (capacity < 1 || capacity > 64) throw new ArgumentOutOfRangeException(nameof(capacity));
            _Logger = logger ?? NullLogger.Instance;
            _Database = database; _Runner = runner; _Capacity = new SemaphoreSlim(capacity, capacity);
        }

        /// <summary>Create and start a durable run; duplicate keys return the original run.</summary>
        public async Task<DebateRun> StartAsync(RequestContext context, string conversationId, CreateRunRequest request, CancellationToken token = default)
        {
            SemaphoreSlim gate = Gate(conversationId); await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DebateRun run = await _Database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false);
                if (run.State == "queued") StartWorker(context, run);
                _Logger.LogInformation("Run accepted {RunId} {ConversationId} {Mode}", run.Id, conversationId, run.Mode);
                return run;
            }
            finally { gate.Release(); }
        }

        /// <summary>Apply a validated moderator command. Holds are per client and expire after thirty seconds.</summary>
        public async Task ApplyAsync(RequestContext context, string conversationId, string runId, CommandRequest request, string clientId, CancellationToken token = default)
        {
            DatabaseDriverBase.RequireWrite(context);
            SemaphoreSlim gate = Gate(conversationId); await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DebateRun run = await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
                if (request.Revision.HasValue && request.Revision.Value != run.Revision) throw new DomainException(409, "revision_conflict", "Run changed. Refresh its state.");
                _Workers.TryGetValue(runId, out ExecutionWorker? worker);
                switch (request.Action)
                {
                    case "hold":
                        if (worker != null && run.State == "window") worker.Holds[clientId] = DateTime.UtcNow.AddSeconds(30);
                        return;
                    case "release":
                        worker?.Holds.TryRemove(clientId, out DateTime _); return;
                    case "note":
                    case "steer":
                        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new DomainException(400, "invalid_message", "Text and idempotency key are required.");
                        if (request.Action == "steer" && (run.Mode == "followup" || run.State == "completed" || run.State == "cancelled" || run.State == "failed")) throw Conflict();
                        if (request.Action == "steer" && !run.Participants.Any(profile => profile.Id == request.RecipientId)) throw new DomainException(400, "invalid_recipient", "Choose a participant snapshot from this run.");
                        await _Database.AppendMessageAsync(context, new Message { ConversationId = conversationId, RunId = runId, Content = request.Text, Kind = request.Action, RecipientId = request.Action == "steer" ? request.RecipientId : null }, request.IdempotencyKey, token).ConfigureAwait(false);
                        return;
                    case "pause":
                        if (run.State == "agent_turn" && worker != null) { worker.PauseAfterTurn = true; return; }
                        if (run.State == "window" || run.State == "queued") { run.State = "paused"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false); return; }
                        throw Conflict();
                    case "interrupt":
                        if (worker?.Generation == null) throw Conflict();
                        worker.PauseAfterTurn = true; worker.Generation.Cancel(); return;
                    case "stop":
                        if (run.State == "completed" || run.State == "cancelled" || run.State == "failed") throw Conflict();
                        if (worker != null && !worker.Completion.IsCompleted) { worker.StopRequested = true; worker.Generation?.Cancel(); }
                        else { run.State = "cancelled"; run.Outcome = "stopped"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false); }
                        return;
                    case "continue":
                        if (run.State == "window" && worker != null) { worker.SkipWindow = true; return; }
                        goto case "resume";
                    case "resume":
                        if (run.State != "paused") throw Conflict();
                        run.State = "queued"; run.Outcome = ""; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false);
                        StartWorker(context, run); return;
                    case "extend":
                        if (run.Mode == "followup") throw new DomainException(409, "invalid_state", "Follow-up answers cannot be extended into debate rounds.");
                        if (request.Rounds < 1 || run.MaxRounds + request.Rounds > 50) throw new DomainException(400, "invalid_rounds", "Extension must keep the total at or below fifty rounds.");
                        run.MaxRounds += request.Rounds;
                        if (run.State == "completed" && run.Outcome == "max_rounds") { run.State = "queued"; run.Outcome = ""; }
                        await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false);
                        if (run.State == "queued") StartWorker(context, run); return;
                    default: throw new DomainException(400, "unknown_action", "Unknown moderator action.");
                }
            }
            finally { gate.Release(); }
        }

        private void StartWorker(RequestContext context, DebateRun run)
        {
            if (_Workers.TryGetValue(run.Id, out ExecutionWorker? current) && !current.Completion.IsCompleted) return;
            ExecutionWorker worker = new ExecutionWorker(); _Workers[run.Id] = worker;
            ProductTelemetry.Runs.Add(1, new KeyValuePair<string, object?>("mode", run.Mode));
            worker.Completion = Task.Run(() => ExecuteAsync(context, run, worker, _Lifetime.Token), CancellationToken.None);
        }

        private async Task ExecuteAsync(RequestContext context, DebateRun run, ExecutionWorker worker, CancellationToken token)
        {
            using (Activity? activity = ProductTelemetry.Activities.StartActivity("conversation.execute"))
            {
                activity?.SetTag("arguebot.run.id", run.Id);
                bool entered = false;
                try
                {
                    await _Capacity.WaitAsync(token).ConfigureAwait(false); entered = true;
                    Conversation conversation = await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
                    while (run.Round <= run.MaxRounds)
                    {
                        SemaphoreSlim gate = Gate(run.ConversationId); await gate.WaitAsync(token).ConfigureAwait(false);
                        RunnerProfile profile;
                        List<Message> contextMessages;
                        Message answer;
                        try
                        {
                            run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                            if (worker.StopRequested) { run.State = "cancelled"; run.Outcome = "stopped"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false); return; }
                            if (run.State == "paused") return;
                            profile = run.Participants[run.SpeakerIndex];
                            contextMessages = (await _Database.ReadMessagesAsync(context, run.ConversationId, token).ConfigureAwait(false)).Where(message => message.State == "completed" && (message.RecipientId == null || message.RecipientId == profile.Id)).ToList();
                            foreach (Message directive in contextMessages.Where(message => message.RecipientId != null))
                            {
                                directive.State = "consumed";
                                await _Database.UpdateMessageAsync(context, directive, "", token).ConfigureAwait(false);
                            }
                            run.State = "agent_turn"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false);
                            answer = await _Database.AppendMessageAsync(context, new Message { ConversationId = run.ConversationId, RunId = run.Id, Speaker = profile.Name, Kind = run.Mode == "followup" ? "final" : run.Round == 1 && run.SpeakerIndex == 0 ? "opening" : "rebuttal", State = "streaming" }, token: token).ConfigureAwait(false);
                            worker.Generation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        }
                        finally { gate.Release(); }
                        string? failure = null;
                        try
                        {
                            string prompt = "You are " + profile.Name + ", in a structured technical debate. Original question: " + conversation.Title + ". Challenge assumptions, give concrete reasons and alternatives, and concede sound points. Only when EVERY participant agrees on the substantive position, write WE HAVE CONSENSUS on its own line followed by the agreed position. Agreement is an agent declaration, not proof of truth.";
                            if (run.Mode == "followup") prompt = "You are " + profile.Name + ". Answer the user's latest follow-up question using the completed conversation below. Distinguish agent-declared agreement from unresolved trade-offs. Be concrete and actionable. Original question: " + conversation.Title;
                            await _Runner.GenerateAsync(profile, prompt, contextMessages, async (delta, execution) =>
                            {
                                answer.Content += delta;
                                await _Database.UpdateMessageAsync(context, answer, delta, execution).ConfigureAwait(false);
                            }, worker.Generation.Token).ConfigureAwait(false);
                            answer.State = "completed";
                        }
                        catch (OperationCanceledException) { answer.State = "interrupted"; }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        { answer.State = "failed"; failure = ex is DomainException domain ? domain.Code : ex.GetType().Name; }
                        finally { worker.Generation.Dispose(); worker.Generation = null; }
                        if (failure != null) _Logger.LogWarning("Generation failed {RunId} {Outcome}", run.Id, failure);
                        else _Logger.LogInformation("Generation finished {RunId} {State}", run.Id, answer.State);
                        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            await _Database.UpdateMessageAsync(context, answer, "", CancellationToken.None).ConfigureAwait(false);
                            run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, CancellationToken.None).ConfigureAwait(false);
                            if (answer.State == "completed")
                            {
                                run.SpeakerIndex++;
                                if (run.SpeakerIndex == run.Participants.Count) { run.SpeakerIndex = 0; run.Round++; }
                            }
                            if (worker.StopRequested) { run.State = "cancelled"; run.Outcome = "stopped"; }
                            else if (token.IsCancellationRequested || worker.PauseAfterTurn || answer.State == "interrupted") { run.State = "paused"; run.Outcome = "interrupted"; }
                            else if (failure != null) { run.State = "paused"; run.Outcome = failure; }
                            else if (run.Mode == "debate" && Regex.IsMatch(answer.Content, @"^\s*WE HAVE CONSENSUS\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)) { run.State = "completed"; run.Outcome = "consensus"; }
                            else if (run.Round > run.MaxRounds) { run.State = "completed"; run.Outcome = "max_rounds"; }
                            else run.State = "window";
                            await _Database.Runs.SaveAsync(context, run, CancellationToken.None).ConfigureAwait(false);
                            if (run.State != "window") return;
                        }
                        finally { gate.Release(); }
                        DateTime deadline = DateTime.UtcNow.AddSeconds(run.InterventionSeconds);
                        while (DateTime.UtcNow < deadline && !worker.SkipWindow && !worker.StopRequested)
                        {
                            token.ThrowIfCancellationRequested();
                            DateTime now = DateTime.UtcNow;
                            foreach (KeyValuePair<string, DateTime> hold in worker.Holds) if (hold.Value <= now) worker.Holds.TryRemove(hold.Key, out DateTime _);
                            if (!worker.Holds.IsEmpty) deadline = now.AddSeconds(run.InterventionSeconds);
                            await Task.Delay(100, token).ConfigureAwait(false);
                            DebateRun latest = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                            if (latest.State == "paused") return;
                        }
                        worker.SkipWindow = false;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    try { run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, CancellationToken.None).ConfigureAwait(false); run.State = "paused"; run.Outcome = "execution_error"; await _Database.Runs.SaveAsync(context, run, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception storage) when (storage is not OutOfMemoryException) { /* Startup reconciliation handles an unavailable store. */ }
                }
                finally { if (entered) _Capacity.Release(); _Logger.LogInformation("Worker detached {RunId}", run.Id); }
            }
        }
        private SemaphoreSlim Gate(string conversationId) { return _Gates.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1)); }
        private static DomainException Conflict() { return new DomainException(409, "invalid_state", "That action is not available in the current run state."); }

        /// <summary>Cancel service-owned work, await checkpoint cleanup, then release resources.</summary>
        public async ValueTask DisposeAsync()
        {
            _Lifetime.Cancel();
            await Task.WhenAll(_Workers.Values.Select(worker => worker.Completion)).ConfigureAwait(false);
            foreach (SemaphoreSlim gate in _Gates.Values) gate.Dispose();
            _Capacity.Dispose(); _Lifetime.Dispose();
        }
    }
}
