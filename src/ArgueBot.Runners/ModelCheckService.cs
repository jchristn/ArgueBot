namespace ArgueBot.Runners
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicit bounded checks share the service runner and endpoint gates; never retry uncertain calls.</summary>
    public sealed class ModelCheckService : IAsyncDisposable
    {
        private int _Waiting;
        private readonly DatabaseDriverBase _Database;
        private readonly IConversationToolRunner _Runner;
        private readonly SemaphoreSlim _Slots = new SemaphoreSlim(2, 2);
        private readonly CancellationTokenSource _Shutdown = new CancellationTokenSource();
        /// <summary>Create a checker around the same credentialed runner used by debate execution.</summary>
        public ModelCheckService(DatabaseDriverBase database, IConversationToolRunner runner) { _Database = database; _Runner = runner; }
        /// <summary>Explicitly invoke a saved model, with a harmless tool probe when requested.</summary>
        public async Task<ModelCheck> CheckAsync(RequestContext context, string modelId, ModelCheckRequest request, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref _Waiting) > 64) { Interlocked.Decrement(ref _Waiting); throw new DomainException(429, "check_queue_full", "The model-check queue is full. Wait for existing checks to finish."); }
            try
            {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _Shutdown.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                await _Slots.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    ModelCheck check = await _Database.ModelChecks.CreateAsync(context, modelId, request, deadline.Token).ConfigureAwait(false);
                    if (!check.NewlyAccepted) return check;
                    long started = Stopwatch.GetTimestamp();
                    try
                    {
                        check.State = "running"; await _Database.ModelChecks.UpdateAsync(context, check, deadline.Token).ConfigureAwait(false);
                        List<ConversationToolDefinition> definitions = new List<ConversationToolDefinition>();
                        if (check.CheckTools) definitions.Add(new ConversationToolDefinition { Name = "connection_probe", Description = "Harmless connection check. Call once with value 4 and use the returned result.", Schema = """{"type":"object","properties":{"value":{"type":"integer","enum":[4]}},"required":["value"],"additionalProperties":false}""" });
                        int calls = 0; StringBuilder output = new StringBuilder();
                        await _Runner.GenerateWithContextAsync(check.Snapshot, "This is an explicit connection check. Do not inspect files or use local tools. " + (check.CheckTools ? "Call connection_probe exactly once with value 4, then reply briefly that the result was received." : "Reply briefly with ARGUEBOT_CONNECTION_OK. No tools are needed."), new List<Message>(), definitions, (call, cancellation) =>
                        {
                            cancellation.ThrowIfCancellationRequested();
                            using (JsonDocument arguments = JsonDocument.Parse(call.ArgumentsJson))
                            {
                                if (call.Name != "connection_probe" || Interlocked.Increment(ref calls) != 1 || !arguments.RootElement.TryGetProperty("value", out JsonElement value) || value.GetInt32() != 4) throw new DomainException(400, "invalid_probe", "Only one advertised connection probe is permitted.");
                            }
                            check.ToolsVerified = true; return Task.FromResult("{\"result\":8}");
                        }, false, (text, cancellation) => { cancellation.ThrowIfCancellationRequested(); if (output.Length + text.Length > 8192) throw new DomainException(413, "check_output_limit", "The check exceeded its response limit."); output.Append(text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask, deadline.Token).ConfigureAwait(false);
                        check.CompletionVerified = !string.IsNullOrWhiteSpace(output.ToString());
                        if (!check.CompletionVerified) throw new DomainException(502, "empty_answer", "The model check returned no completion.");
                        if (check.CheckTools && !check.ToolsVerified) throw new DomainException(502, "tool_probe_not_observed", "The model completed without the requested tool probe.");
                        check.State = "completed";
                        if (check.Snapshot.Kind == "cli" && (check.Snapshot.Provider == "claude" || check.Snapshot.Provider == "codex" || check.Snapshot.Provider == "gemini")) check.ExecutableVersion = await VersionAsync(check.Snapshot, deadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { check.State = "cancelled"; check.ErrorCode = "check_cancelled_or_deadline"; }
                    catch (DomainException failure) { check.State = "failed"; check.ErrorCode = failure.Code; }
                    catch (Exception failure) when (failure is not OutOfMemoryException) { check.State = "failed"; check.ErrorCode = "model_check_failed"; }
                    finally
                    {
                        check.DurationMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        await _Database.ModelChecks.UpdateAsync(context, check, CancellationToken.None).ConfigureAwait(false);
                    }
                    return check;
                }
                finally { _Slots.Release(); }
            }
            }
            finally { Interlocked.Decrement(ref _Waiting); }
        }
        private static async Task<string> VersionAsync(RunnerProfile profile, CancellationToken token)
        {
            try
            {
                StringBuilder output = new StringBuilder();
                await new CliRunner().GenerateAsync(new RunnerProfile { Executable = profile.Executable, RestrictEnvironment = profile.RestrictEnvironment, EnvironmentVariables = new List<string>(profile.EnvironmentVariables), Provider = "plain", Arguments = new List<string> { "--version" }, WorkingDirectory = profile.WorkingDirectory, TimeoutSeconds = 5, MaxOutputBytes = 1024 }, "", new List<Message>(), (text, cancellation) => { cancellation.ThrowIfCancellationRequested(); output.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
                Match version = Regex.Match(output.ToString(), @"\b[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}\b", RegexOptions.CultureInvariant);
                return version.Success ? version.Value : "";
            }
            catch (Exception failure) when (failure is not OutOfMemoryException) { return ""; }
        }
        /// <summary>Cancel checks and drain active execution before the service releases its database.</summary>
        public async ValueTask DisposeAsync()
        {
            _Shutdown.Cancel();
            await _Slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            await _Slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _Slots.Dispose(); _Shutdown.Dispose();
        }
    }
}
