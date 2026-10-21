namespace ArgueBot.Runners.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Models;

    internal static class LocalTools
    {
        private const int _MaximumBytes = 65536;

        internal static List<ToolDefinition> Definitions()
        {
            return new List<ToolDefinition>
            {
                Define("read_file", "Read a UTF-8 text file on the service machine, up to 64 KiB. Relative paths use the participant working directory.", """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}"""),
                Define("write_file", "Create or replace a UTF-8 file on the service machine, up to 64 KiB. Relative paths use the participant working directory.", """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"],"additionalProperties":false}"""),
                Define("list_directory", "List up to 200 entries in a local directory. Relative paths use the participant working directory.", """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}"""),
                Define("run_process", "Run a local executable with literal arguments in the participant working directory. No shell parsing; for shell syntax explicitly invoke the shell. Returns exit code, stdout and stderr. Maximum 30 seconds and 64 KiB per output stream.", """{"type":"object","properties":{"executable":{"type":"string"},"arguments":{"type":"array","items":{"type":"string"}}},"required":["executable","arguments"],"additionalProperties":false}""")
            };
        }

        internal static async Task<ToolExecutionResult> ExecuteAsync(ToolCall call, string directory, CancellationToken token)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(call.ArgumentsJson))
                {
                    JsonElement arguments = document.RootElement;
                    token.ThrowIfCancellationRequested();
                    switch (call.Name)
                    {
                        case "read_file":
                            using (FileStream file = new FileStream(Resolve(directory, Text(arguments, "path")), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                            {
                                byte[] bytes = new byte[_MaximumBytes + 1]; int total = 0; int count;
                                while (total < bytes.Length && (count = await file.ReadAsync(bytes.AsMemory(total), token).ConfigureAwait(false)) > 0) total += count;
                                if (total > _MaximumBytes) throw new IOException("File exceeds 64 KiB. Use run_process to inspect a smaller range.");
                                return Result(true, new { content = new UTF8Encoding(false, true).GetString(bytes, 0, total) });
                            }
                        case "write_file":
                            string content = Text(arguments, "content");
                            if (Encoding.UTF8.GetByteCount(content) > _MaximumBytes) throw new IOException("File content exceeds 64 KiB.");
                            string path = Resolve(directory, Text(arguments, "path"));
                            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), token).ConfigureAwait(false);
                            return Result(true, new { path, bytesWritten = Encoding.UTF8.GetByteCount(content) });
                        case "list_directory":
                            string[] entries = Directory.EnumerateFileSystemEntries(Resolve(directory, Text(arguments, "path"))).Take(201).ToArray();
                            return Result(true, new { entries = entries.Take(200).ToArray(), truncated = entries.Length > 200 });
                        case "run_process": return await RunAsync(arguments, directory, token).ConfigureAwait(false);
                        default: throw new ArgumentException("Unknown local tool: " + call.Name);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Result(false, new { error = exception.Message });
            }
        }

        private static ToolDefinition Define(string name, string description, string schema)
        { return ToolDefinition.Function(name, description, JsonSerializer.Deserialize<Dictionary<string, object>>(schema)!); }
        private static ToolExecutionResult Result(bool success, object value)
        {
            string content = JsonSerializer.Serialize(new { success, result = value });
            if (Encoding.UTF8.GetByteCount(content) > 262144) return new ToolExecutionResult { Success = false, Content = "{\"success\":false,\"error\":\"Tool result exceeds 256 KiB. Request a smaller result.\"}" };
            return new ToolExecutionResult { Success = success, Content = content };
        }
        private static string Text(JsonElement arguments, string name)
        { return arguments.GetProperty(name).GetString() ?? throw new ArgumentException(name + " must be a string."); }
        private static string Resolve(string directory, string path) { return Path.GetFullPath(path, directory); }

        private static async Task<ToolExecutionResult> RunAsync(JsonElement arguments, string directory, CancellationToken token)
        {
            string executable = Text(arguments, "executable");
            string[] values = arguments.GetProperty("arguments").EnumerateArray().Select(value => value.GetString() ?? throw new ArgumentException("Process arguments must be strings.")).ToArray();
            if (string.IsNullOrWhiteSpace(executable) || executable.Contains('\0') || values.Length > 128 || values.Any(value => value.Contains('\0')) || values.Sum(value => (long)value.Length) + executable.Length > 32768) throw new ArgumentException("Use an executable and at most 128 literal arguments, without NUL characters, totaling at most 32768 characters.");
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (Process process = new Process())
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                process.StartInfo = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true };
                foreach (string value in values) process.StartInfo.ArgumentList.Add(value);
                if (!process.Start()) throw new IOException("The tool process could not start.");
                using (CancellationTokenRegistration registration = deadline.Token.Register(() => Kill(process)))
                {
                    process.StandardInput.Close();
                    try
                    {
                        Task<string> output = CaptureAsync(process.StandardOutput, process, deadline.Token);
                        Task<string> errors = CaptureAsync(process.StandardError, process, deadline.Token);
                        await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        return Result(process.ExitCode == 0, new { exitCode = process.ExitCode, stdout = await output.ConfigureAwait(false), stderr = await errors.ConfigureAwait(false) });
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    { return Result(false, new { error = "Local process exceeded its 30 second limit and was stopped." }); }
                    finally { Kill(process); }
                }
            }
        }

        private static async Task<string> CaptureAsync(StreamReader reader, Process process, CancellationToken token)
        {
            try
            {
                StringBuilder text = new StringBuilder(); char[] buffer = new char[4096]; int bytes = 0; int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    bytes += Encoding.UTF8.GetByteCount(buffer, 0, count);
                    if (bytes > _MaximumBytes) throw new IOException("Local process output exceeded 64 KiB and the process was stopped.");
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
            catch { Kill(process); throw; }
        }
        private static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
