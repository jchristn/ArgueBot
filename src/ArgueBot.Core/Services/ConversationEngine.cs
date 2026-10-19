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
        private readonly TimeProvider _Clock;
        private readonly int _StreamBatchCharacters;
        private readonly TimeSpan _StreamCommitInterval;
        private readonly int _ContextMaxCharacters;
        private readonly int _ContextMaxMessages;
        private readonly CancellationTokenSource _Lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _Capacity;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Gates = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly ConcurrentDictionary<string, ExecutionWorker> _Workers = new ConcurrentDictionary<string, ExecutionWorker>();

        /// <summary>Create an engine with bounded concurrency, injectable time and bounded stream commit batches. Defaults: four runs, 4096 characters or 250 milliseconds per commit, and context bounded to 262144 UTF-16 characters plus 1000 messages. Oversized context is refused before invocation.</summary>
        public ConversationEngine(DatabaseDriverBase database, IRunner runner, int capacity = 4, ILogger? logger = null, TimeProvider? clock = null, int streamBatchCharacters = 4096, int streamCommitMilliseconds = 250, int contextMaxCharacters = 262144, int contextMaxMessages = 1000)
        {
            ArgumentNullException.ThrowIfNull(database); ArgumentNullException.ThrowIfNull(runner);
            if (capacity < 1 || capacity > 64) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (streamBatchCharacters < 1 || streamBatchCharacters > 65536) throw new ArgumentOutOfRangeException(nameof(streamBatchCharacters));
            if (streamCommitMilliseconds < 10 || streamCommitMilliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(streamCommitMilliseconds));
            if (contextMaxCharacters < 1024 || contextMaxCharacters > 16777216) throw new ArgumentOutOfRangeException(nameof(contextMaxCharacters));
            if (contextMaxMessages < 1 || contextMaxMessages > 10000) throw new ArgumentOutOfRangeException(nameof(contextMaxMessages));
            _ContextMaxCharacters = contextMaxCharacters; _ContextMaxMessages = contextMaxMessages;
            _Clock = clock ?? TimeProvider.System; _StreamBatchCharacters = streamBatchCharacters; _StreamCommitInterval = TimeSpan.FromMilliseconds(streamCommitMilliseconds);
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
                if ((request.Action == "note" || request.Action == "steer") && await _Database.IsDuplicateMessageAsync(context, new Message { ConversationId = conversationId, RunId = runId, Kind = request.Action, Content = request.Text, RecipientId = request.Action == "steer" ? request.RecipientId : null }, request.IdempotencyKey, token).ConfigureAwait(false)) return;
                bool durable = request.Action != "hold" && request.Action != "release" && request.Action != "note" && request.Action != "steer";
                if (durable && await _Database.Runs.IsDuplicateCommandAsync(context, conversationId, runId, request, token).ConfigureAwait(false)) return;
                DebateRun run = await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
                if (request.Revision.HasValue && request.Revision.Value != run.Revision) throw new DomainException(409, "revision_conflict", "Run changed. Refresh its state.");
                _Workers.TryGetValue(runId, out ExecutionWorker? worker);
                switch (request.Action)
                {
                    case "hold":
                        if (worker != null && run.State == "window") worker.Holds[clientId] = _Clock.GetTimestamp();
                        return;
                    case "release":
                        worker?.Holds.TryRemove(clientId, out long _); return;
                    case "note":
                    case "steer":
                        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new DomainException(400, "invalid_message", "Text and idempotency key are required.");
                        if (request.Action == "steer" && (run.Mode == "followup" || run.State == "completed" || run.State == "cancelled" || run.State == "failed")) throw Conflict();
                        if (request.Action == "steer" && !run.Participants.Any(profile => profile.Id == request.RecipientId)) throw new DomainException(400, "invalid_recipient", "Choose a participant snapshot from this run.");
                        await _Database.AppendMessageAsync(context, new Message { ConversationId = conversationId, RunId = runId, Content = request.Text, Kind = request.Action, RecipientId = request.Action == "steer" ? request.RecipientId : null }, request.IdempotencyKey, token).ConfigureAwait(false);
                        return;
                    case "pause":
                        if (run.State == "agent_turn" && worker != null) { run.State = "pausing"; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false); worker.PauseAfterTurn = true; return; }
                        if (run.State == "window" || run.State == "queued") { run.State = "paused"; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false); if (worker != null) worker.PauseAfterTurn = true; return; }
                        throw Conflict();
                    case "interrupt":
                        if (worker?.Generation == null) throw Conflict();
                        run.State = "interrupting"; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false);
                        worker.PauseAfterTurn = true; worker.Generation.Cancel(); return;
                    case "stop":
                        if (run.State == "completed" || run.State == "cancelled" || run.State == "failed") throw Conflict();
                        if (worker != null && !worker.Completion.IsCompleted) { run.State = "stopping"; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false); worker.StopRequested = true; worker.Generation?.Cancel(); }
                        else { run.State = "cancelled"; run.Outcome = "stopped"; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false); }
                        return;
                    case "continue":
                        if (run.State == "window" && worker != null) { await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false); worker.SkipWindow = true; return; }
                        goto case "resume";
                    case "resume":
                        if (run.State != "paused") throw Conflict();
                        run.State = "queued"; run.Outcome = ""; await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false);
                        StartWorker(context, run, true); return;
                    case "extend":
                        if (run.Mode == "followup") throw new DomainException(409, "invalid_state", "Follow-up answers cannot be extended into debate rounds.");
                        if (request.Rounds < 1 || request.Rounds > 50 - run.MaxRounds) throw new DomainException(400, "invalid_rounds", "Extension must keep the total at or below fifty rounds.");
                        run.MaxRounds += request.Rounds;
                        if (run.State == "completed" && run.Outcome == "max_rounds") { run.State = "queued"; run.Outcome = ""; }
                        await _Database.Runs.SaveCommandAsync(context, run, request, token).ConfigureAwait(false);
                        if (run.State == "queued") StartWorker(context, run, true); return;
                    default: throw new DomainException(400, "unknown_action", "Unknown moderator action.");
                }
            }
            finally { gate.Release(); }
        }

        private void StartWorker(RequestContext context, DebateRun run, bool replace = false)
        {
            Task previous = Task.CompletedTask;
            if (_Workers.TryGetValue(run.Id, out ExecutionWorker? current) && !current.Completion.IsCompleted)
            {
                if (!replace) return;
                current.PauseAfterTurn = true;
                previous = current.Completion;
            }
            ExecutionWorker worker = new ExecutionWorker(); _Workers[run.Id] = worker;
            ProductTelemetry.Runs.Add(1, new KeyValuePair<string, object?>("mode", run.Mode));
            worker.Completion = Task.Run(async () => { await previous.ConfigureAwait(false); await ExecuteAsync(context, run, worker, _Lifetime.Token).ConfigureAwait(false); }, CancellationToken.None);
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
                    while (true)
                    {
                        SemaphoreSlim gate = Gate(run.ConversationId); await gate.WaitAsync(token).ConfigureAwait(false);
                        RunnerProfile profile;
                        List<Message> contextMessages;
                        Message answer;
                        GenerationAttempt attempt;
                        string prompt;
                        try
                        {
                            run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                            if (worker.StopRequested) { run.State = "cancelled"; run.Outcome = "stopped"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false); return; }
                            if (worker.PauseAfterTurn || run.State == "paused" || run.State == "completed" || run.State == "cancelled" || run.State == "failed") return;
                            if (run.Round > run.MaxRounds) { run.State = "completed"; run.Outcome = "max_rounds"; await _Database.Runs.SaveAsync(context, run, token).ConfigureAwait(false); return; }
                            profile = run.Participants[run.SpeakerIndex];
                            contextMessages = (await _Database.ReadMessagesAsync(context, run.ConversationId, token).ConfigureAwait(false)).Where(message => message.State == "completed" && (message.RecipientId == null || message.RecipientId == profile.Id)).ToList();
                            prompt = "You are " + profile.Name + ", in a structured technical debate. Original question: " + run.Question + ". Challenge assumptions, give concrete reasons and alternatives, and concede sound points. Only when EVERY participant agrees on the substantive position, write WE HAVE CONSENSUS on its own line followed by the agreed position. Agreement is an agent declaration, not proof of truth.";
                            if (run.Mode == "followup") prompt = "You are " + profile.Name + ". Answer the user's latest follow-up question using the completed conversation below. Distinguish agent-declared agreement from unresolved trade-offs. Be concrete and actionable. Original question: " + run.Question;
                            GenerationWork work = await _Database.Attempts.BeginAsync(context, run, contextMessages, prompt, _ContextMaxCharacters, _ContextMaxMessages, token).ConfigureAwait(false);
                            answer = work.Answer; attempt = work.Attempt;
                            worker.Generation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        }
                        finally { gate.Release(); }
                        long generationStarted = _Clock.GetTimestamp();
                        string? failure = null;
                        string answerState = "completed";
                        await using (MessageStreamWriter stream = new MessageStreamWriter(_Database, context, answer, _StreamBatchCharacters, _StreamCommitInterval, _Clock))
                        using (CancellationTokenRegistration streamFailure = stream.FaultToken.Register(() => worker.Generation?.Cancel()))
                        {
                        try
                        {
                            await _Database.Attempts.MarkStreamingAsync(context, attempt, worker.Generation.Token).ConfigureAwait(false);
                            await _Runner.GenerateAsync(profile, prompt, contextMessages, stream.AppendAsync, worker.Generation.Token).ConfigureAwait(false);
                            if (stream.Failed) throw new DomainException(503, "stream_persistence_failed", "Stream persistence failed.");
                            answerState = "completed";
                        }
                        catch (OperationCanceledException) { answerState = stream.Failed ? "failed" : "interrupted"; if (stream.Failed) failure = "stream_persistence_failed"; }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        { answerState = "failed"; failure = ex is DomainException domain ? domain.Code : ex.GetType().Name; }
                        }
                        answer.State = answerState;
                        attempt.ErrorCode = failure ?? "";
                        attempt.DurationMilliseconds = Math.Max(0, (long)_Clock.GetElapsedTime(generationStarted).TotalMilliseconds);
                        if (failure != null) _Logger.LogWarning("Generation failed {RunId} {Outcome}", run.Id, failure);
                        else _Logger.LogInformation("Generation finished {RunId} {State}", run.Id, answer.State);
                        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            worker.Generation?.Dispose(); worker.Generation = null;
                            run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, CancellationToken.None).ConfigureAwait(false);
                            if (answer.State == "completed")
                            {
                                run.SpeakerIndex++;
                                if (run.SpeakerIndex == run.Participants.Count) { run.SpeakerIndex = 0; run.Round++; }
                            }
                            if (worker.StopRequested) { run.State = "cancelled"; run.Outcome = "stopped"; }
                            else if (answer.State == "completed" && run.Mode == "debate" && Regex.IsMatch(answer.Content, @"^\s*WE HAVE CONSENSUS\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)) { run.State = "completed"; run.Outcome = "consensus"; }
                            else if (answer.State == "completed" && run.Round > run.MaxRounds) { run.State = "completed"; run.Outcome = "max_rounds"; }
                            else if (failure != null) { run.State = "paused"; run.Outcome = failure; }
                            else if (token.IsCancellationRequested || worker.PauseAfterTurn || answer.State == "interrupted") { run.State = "paused"; run.Outcome = answer.State == "interrupted" ? "interrupted" : "paused"; }
                            else run.State = "window";
                            await _Database.Attempts.CompleteAsync(context, run, attempt, answer, CancellationToken.None).ConfigureAwait(false);
                            if (run.State != "window") return;
                        }
                        finally { gate.Release(); }
                        long windowStarted = _Clock.GetTimestamp();
                        while (!worker.SkipWindow && !worker.StopRequested && !worker.PauseAfterTurn)
                        {
                            token.ThrowIfCancellationRequested();
                            long now = _Clock.GetTimestamp();
                            foreach (KeyValuePair<string, long> hold in worker.Holds) if (_Clock.GetElapsedTime(hold.Value, now) >= TimeSpan.FromSeconds(30)) worker.Holds.TryRemove(hold.Key, out long _);
                            if (!worker.Holds.IsEmpty) windowStarted = now;
                            else if (_Clock.GetElapsedTime(windowStarted, now) >= TimeSpan.FromSeconds(run.InterventionSeconds)) break;
                            await Task.Delay(TimeSpan.FromMilliseconds(100), _Clock, token).ConfigureAwait(false);
                            DebateRun latest = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                            if (latest.State == "paused" || worker.PauseAfterTurn) return;
                        }
                        worker.SkipWindow = false;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    try { run = await _Database.Runs.ReadAsync(context, run.ConversationId, run.Id, CancellationToken.None).ConfigureAwait(false); run.State = "paused"; run.Outcome = ex is DomainException domain ? domain.Code : "execution_error"; await _Database.Runs.SaveAsync(context, run, CancellationToken.None).ConfigureAwait(false); }
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
