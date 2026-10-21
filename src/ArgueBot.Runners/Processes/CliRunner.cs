namespace ArgueBot.Runners.Processes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;

    /// <summary>Direct process runner. Prompts go through stdin; both pipes are drained concurrently and bounded.</summary>
    public sealed class CliRunner : IRunner
    {
        /// <inheritdoc />
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (Process process = new Process())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
                CancellationToken execution = timeout.Token;
                ProcessStartInfo start = new ProcessStartInfo(profile.Executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true };
                if (profile.WorkingDirectory.Length > 0) start.WorkingDirectory = profile.WorkingDirectory;
                List<string> arguments = profile.Arguments.Count > 0 ? profile.Arguments : Defaults(profile.Provider);
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                process.StartInfo = start;
                if (!process.Start()) throw new IOException("Agent process could not start.");
                using (CancellationTokenRegistration registration = execution.Register(() => Kill(process)))
                {
                    StringBuilder prompt = new StringBuilder(instructions);
                    foreach (Message message in history) prompt.Append("\n\n[").Append(message.Speaker).Append("]\n").Append(message.Content);
                    Task input = FailFastAsync(SendInputAsync(process, prompt.ToString(), execution), process);
                    Task output = FailFastAsync(ReadOutputAsync(process.StandardOutput, profile, onText, execution), process);
                    Task errors = FailFastAsync(DrainErrorsAsync(process.StandardError, profile.MaxOutputBytes, execution), process);
                    try
                    {
                        await Task.WhenAll(input, output, errors, process.WaitForExitAsync(execution)).ConfigureAwait(false);
                        execution.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0) throw new DomainException(502, "agent_exit", "Agent process exited with code " + process.ExitCode + ". Check its authentication and configuration.");
                    }
                    catch { Kill(process); throw; }
                    finally { if (!process.HasExited) Kill(process); }
                }
            }
        }
        private static async Task FailFastAsync(Task operation, Process process)
        {
            try { await operation.ConfigureAwait(false); }
            catch { Kill(process); throw; }
        }
        private static List<string> Defaults(string provider)
        {
            return HarnessDefaults.Create(provider).Arguments;
        }
        private static async Task SendInputAsync(Process process, string text, CancellationToken token)
        {
            try { await process.StandardInput.WriteAsync(text.AsMemory(), token).ConfigureAwait(false); process.StandardInput.Close(); }
            catch (IOException) when (process.HasExited) { }
        }
        private static async Task DrainErrorsAsync(StreamReader reader, int maximum, CancellationToken token)
        {
            char[] buffer = new char[4096]; int total = 0; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            { total += Encoding.UTF8.GetByteCount(buffer, 0, count); if (total > maximum) throw new IOException("Agent stderr exceeded the configured capture limit."); }
        }
        private static async Task ReadOutputAsync(StreamReader reader, RunnerProfile profile, Func<string, CancellationToken, Task> onText, CancellationToken token)
        {
            char[] buffer = new char[2048]; StringBuilder line = new StringBuilder(); StringBuilder emitted = new StringBuilder(); Dictionary<string, string> codexItems = new Dictionary<string, string>(StringComparer.Ordinal); int total = 0; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                total += Encoding.UTF8.GetByteCount(buffer, 0, count);
                if (total > profile.MaxOutputBytes) throw new IOException("Agent stdout exceeded the configured capture limit.");
                string text = new string(buffer, 0, count);
                if (profile.Provider == "plain" || profile.Provider == "mux") { await onText(text, token).ConfigureAwait(false); continue; }
                foreach (char character in text)
                {
                    if (character == '\n') { await EmitLineAsync(line.ToString(), profile.Provider, emitted, codexItems, onText, token).ConfigureAwait(false); line.Clear(); }
                    else if (character != '\r') line.Append(character);
                }
            }
            if (line.Length > 0) await EmitLineAsync(line.ToString(), profile.Provider, emitted, codexItems, onText, token).ConfigureAwait(false);
        }
        private static async Task EmitLineAsync(string line, string provider, StringBuilder emitted, Dictionary<string, string> codexItems, Func<string, CancellationToken, Task> onText, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            CliWireEvent item;
            try { item = JsonSerializer.Deserialize<CliWireEvent>(line) ?? throw new InvalidDataException("Agent emitted a null event."); }
            catch (JsonException ex) { throw new InvalidDataException("Agent emitted malformed JSONL.", ex); }
            if (provider == "codex" && item.Item?.Type == "agent_message" && !string.IsNullOrEmpty(item.Item.Text))
            {
                string id = item.Item.Id ?? "legacy-agent-message";
                string snapshotText = item.Item.Text;
                bool existing = codexItems.TryGetValue(id, out string? previous);
                previous ??= "";
                if (!snapshotText.StartsWith(previous, StringComparison.Ordinal)) throw new InvalidDataException("Agent message snapshot conflicts with earlier text for the same item.");
                string suffix = snapshotText.Substring(previous.Length);
                if (suffix.Length == 0) return;
                if (!existing && codexItems.Count > 0) await onText("\n\n", token).ConfigureAwait(false);
                await onText(suffix, token).ConfigureAwait(false);
                codexItems[id] = snapshotText;
                return;
            }
            string? delta = item.Event?.Delta?.Text ?? (item.Type == "text_delta" ? item.Text : null);
            if (item.Type == "message" && item.Role == "assistant") delta = item.Text;
            string? snapshot = item.Result ?? (item.Item?.Type == "agent_message" ? item.Item.Text : null);
            if (item.Type == "assistant" && item.Message?.Content != null)
            {
                StringBuilder body = new StringBuilder(); foreach (CliWireEvent part in item.Message.Content) if (part.Type == "text") body.Append(part.Text); snapshot = body.ToString();
            }
            if (delta != null) { emitted.Append(delta); await onText(delta, token).ConfigureAwait(false); }
            else if (!string.IsNullOrEmpty(snapshot))
            {
                string prior = emitted.ToString();
                if (!snapshot.StartsWith(prior, StringComparison.Ordinal)) throw new InvalidDataException("Agent final snapshot conflicts with streamed content.");
                string suffix = snapshot.Substring(prior.Length); emitted.Append(suffix);
                if (suffix.Length > 0) await onText(suffix, token).ConfigureAwait(false);
            }
        }
        private static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
