namespace ArgueBot.Tests.Fixtures
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            string mode = args.Length > 0 ? args[0] : "plain";
            if (mode == "certificate" && args.Length == 2) { CertificateFixture.Write(args[1]); return 0; }
            string input = await Console.In.ReadToEndAsync().ConfigureAwait(false);
            if (mode == "environment" || mode == "environment-echo")
            {
                if (Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_ALLOWED") != "fictional-allowed" || Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_SECRET") != null || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PATH")) || Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH") != "fictional-git-bash" || Environment.GetEnvironmentVariable("OneDriveCommercial") != "fictional-onedrive") return 3;
                Console.WriteLine(mode == "environment-echo" ? "allowed=" + Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_ALLOWED") + ";token=" + Environment.GetEnvironmentVariable("ARGUEBOT_FIXTURE_ACCESS_TOKEN") : "Allowed environment verified."); return 0;
            }
            if (mode == "model-check")
            {
                Console.WriteLine(input.Contains("connection_probe", StringComparison.Ordinal) && !input.Contains("ArgueBot tool", StringComparison.Ordinal) ? "{\"contextTool\":{\"name\":\"connection_probe\",\"argumentsJson\":\"{\\\"value\\\":4}\"}}" : "ARGUEBOT_CONNECTION_OK"); return 0;
            }
            if (mode == "mcp-claude" || mode == "mcp-codex") return await McpClientFixture.RunAsync(args).ConfigureAwait(false);
            if (mode == "mcp-resources-claude" || mode == "mcp-resources-codex") return await McpClientFixture.ResourcesAsync(args).ConfigureAwait(false);
            if (mode == "mcp-questions-claude" || mode == "mcp-questions-codex") return await McpClientFixture.QuestionsAsync(args).ConfigureAwait(false);
            if (mode == "envelope-questions")
            {
                // A generic CLI that asks the user through the JSON tool envelope and answers with the reply.
                if (input.Contains("ask_user: {", StringComparison.Ordinal)) Console.WriteLine(input.Contains("\"answered\"", StringComparison.Ordinal) && input.Contains("Every environment", StringComparison.Ordinal) ? "Envelope question answered: Every environment." : input.Contains("\"parked\"", StringComparison.Ordinal) ? "Envelope question parked." : "Envelope question failed.");
                else if (input.Contains("\"ask_user\"", StringComparison.Ordinal)) Console.WriteLine("{\"contextTool\":{\"name\":\"ask_user\",\"argumentsJson\":\"{\\\"questions\\\":[{\\\"key\\\":\\\"scope\\\",\\\"header\\\":\\\"Scope\\\",\\\"prompt\\\":\\\"Which environments?\\\",\\\"kind\\\":\\\"single_choice\\\",\\\"options\\\":[{\\\"id\\\":\\\"prod\\\",\\\"label\\\":\\\"Production only\\\"},{\\\"id\\\":\\\"all\\\",\\\"label\\\":\\\"Every environment\\\"}]}]}\"}}");
                else Console.WriteLine("No question tool offered.");
                return 0;
            }
            if (mode == "envelope-resources")
            {
                // A generic CLI that uses the JSON tool envelope: read a resource, append a note, then answer with what it read.
                if (input.Contains("append_resource: {", StringComparison.Ordinal)) Console.WriteLine("Envelope resources verified: " + (input.Contains("Invoices go out on the 1st", StringComparison.Ordinal) ? "read billing" : "billing missing") + (input.Contains("\"success\":true", StringComparison.Ordinal) ? ", note saved." : ", note failed."));
                else if (input.Contains("read_resource: {", StringComparison.Ordinal)) Console.WriteLine("{\"contextTool\":{\"name\":\"append_resource\",\"argumentsJson\":\"{\\\"name\\\":\\\"debate-notes\\\",\\\"text\\\":\\\"Fixture note via envelope.\\\"}\"}}");
                else if (input.Contains("\"read_resource\"", StringComparison.Ordinal)) Console.WriteLine("{\"contextTool\":{\"name\":\"read_resource\",\"argumentsJson\":\"{\\\"name\\\":\\\"billing\\\"}\"}}");
                else Console.WriteLine("No resource tools offered.");
                return 0;
            }
            if (mode == "claude-tool-replay")
            {
                byte[] replay = await File.ReadAllBytesAsync(args[1]).ConfigureAwait(false);
                foreach (byte value in replay) await Console.OpenStandardOutput().WriteAsync(new byte[] { value }).ConfigureAwait(false);
                return 0;
            }
            if (mode.StartsWith("claude-", StringComparison.Ordinal))
            {
                if (mode == "claude-empty") { Console.WriteLine("{\"type\":\"result\",\"subtype\":\"success\",\"result\":\"\"}"); return 0; }
                if (mode == "claude-error-result") { Console.WriteLine("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"Fictional provider error\"}"); return 0; }
                if (mode == "claude-snapshots")
                {
                    Console.WriteLine("{\"type\":\"assistant\",\"message\":{\"id\":\"first\",\"content\":[{\"type\":\"text\",\"text\":\"First answer.\"}]}}");
                    Console.WriteLine("{\"type\":\"assistant\",\"message\":{\"id\":\"final\",\"content\":[{\"type\":\"text\",\"text\":\"Final answer.\"}]}}");
                    Console.WriteLine("{\"type\":\"result\",\"result\":\"Final answer.\"}");
                    return 0;
                }
                Console.WriteLine("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Partial answer\"}}}");
                if (mode == "claude-conflict") Console.WriteLine("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Conflicting answer\"}]}}");
                if (mode == "claude-result-conflict") Console.WriteLine("{\"type\":\"result\",\"result\":\"Conflicting answer\"}");
                return 0;
            }
            if (mode == "tool-report") { Console.WriteLine(JsonSerializer.Serialize(args)); return 0; }
            if (mode == "tool-child") { await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString()).ConfigureAwait(false); await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
            if (mode == "tool-hang")
            {
                ProcessStartInfo child = new ProcessStartInfo("dotnet") { UseShellExecute = false };
                child.ArgumentList.Add(typeof(Program).Assembly.Location); child.ArgumentList.Add("tool-child"); child.ArgumentList.Add(args[1] + ".child");
                using (Process process = Process.Start(child)!)
                {
                    while (!File.Exists(args[1] + ".child")) await Task.Delay(10).ConfigureAwait(false);
                    await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString()).ConfigureAwait(false);
                    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
                }
                return 0;
            }
            if (mode == "mux")
            {
                if (args.Length != 3 || args[1] != "--endpoint" || args[2] != "fixture endpoint" || !input.Contains("Literal $(echo unsafe) `prompt` 議論", StringComparison.Ordinal)) return 19;
                await Console.Error.WriteAsync("Mux fixture diagnostic, not an answer.").ConfigureAwait(false);
                byte[] bytes = Encoding.UTF8.GetBytes("Mux answer 議論.\n");
                foreach (byte value in bytes) await Console.OpenStandardOutput().WriteAsync(new byte[] { value }).ConfigureAwait(false);
                return 0;
            }
            if (mode == "codex-messages" || mode == "codex-conflict")
            {
                Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fixture-thread\"}");
                // Codex reports configuration warnings as error items whose message is plain text, not an object.
                Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_w\",\"type\":\"error\",\"message\":\"Codex is ignoring 1 unrecognized configuration setting.\\n  user (/home/fixture/.codex/config.toml): `hooks.fixture` is ignored.\"}}");
                // Tool and search items reuse field names with object values; ArgueBot must skip what it does not use.
                Console.WriteLine("{\"type\":\"item.started\",\"item\":{\"id\":\"item_t\",\"type\":\"mcp_tool_call\",\"server\":\"arguebot\",\"tool\":\"get_debate_state\",\"arguments\":{},\"result\":null,\"error\":null,\"status\":\"in_progress\"}}");
                Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_t\",\"type\":\"mcp_tool_call\",\"server\":\"arguebot\",\"tool\":\"get_debate_state\",\"arguments\":{},\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"success\\\":true}\"}],\"structured_content\":null},\"error\":null,\"status\":\"completed\"}}");
                Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_s\",\"type\":\"web_search\",\"query\":\"fixture\",\"action\":{\"type\":\"search\",\"queries\":[\"fixture\"]},\"results\":[{\"type\":\"text_result\",\"title\":\"Fixture\",\"url\":\"https://example.invalid/\"}]}}");
                Console.WriteLine("{\"type\":\"item.updated\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"Checking\"}}");
                Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"Checking the explanation.\"}}");
                Console.WriteLine(mode == "codex-conflict"
                    ? "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"Conflicting replacement.\"}}"
                    : "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"Final answer 議論.\"}}");
                Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"Checking the explanation.\"}}");
                return 0;
            }
            if (mode == "diagnostic-schema" || mode == "diagnostic-exit" || mode == "diagnostic-large" || mode == "diagnostic-timeout")
            {
                await Console.Error.WriteLineAsync("Fictional harness diagnostic 議論").ConfigureAwait(false);
                await Console.Error.FlushAsync().ConfigureAwait(false);
                if (mode == "diagnostic-exit") return 17;
                if (mode == "diagnostic-timeout") { await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
                if (mode == "diagnostic-large") Console.WriteLine("{invalid" + new string('x', 20000) + "TRIGGER-END");
                else Console.WriteLine("{\"type\":\"assistant\",\"message\":{\"content\":\"unexpected string\"}}");
                return 0;
            }
            if (mode == "exit") return 17;
            if (mode == "hang") { await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
            if (mode == "stderr") { for (int index = 0; index < 300; index++) await Console.Error.WriteAsync(new string('x', 4096)).ConfigureAwait(false); return 0; }
            if (mode == "jsonl")
            {
                string record = "{\"type\":\"stream_event\",\"event\":{\"delta\":{\"text\":\"Hello 議論\"}}}\n{\"type\":\"result\",\"result\":\"Hello 議論\"}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(record);
                foreach (byte value in bytes) { await Console.OpenStandardOutput().WriteAsync(new byte[] { value }).ConfigureAwait(false); }
                return 0;
            }
            if (mode == "malformed-hang") { Console.WriteLine("{invalid"); await Console.Out.FlushAsync().ConfigureAwait(false); await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
            if (mode == "stdout-hang") { await Console.Out.WriteAsync(new string('x', 16384)).ConfigureAwait(false); await Console.Out.FlushAsync().ConfigureAwait(false); await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
            if (mode == "stderr-hang") { await Console.Error.WriteAsync(new string('x', 16384)).ConfigureAwait(false); await Console.Error.FlushAsync().ConfigureAwait(false); await Task.Delay(Timeout.Infinite).ConfigureAwait(false); return 0; }
            if (mode == "malformed") { Console.WriteLine("{invalid"); return 0; }
            string answer = mode == "consensus" ? "WE HAVE CONSENSUS\nAgreed fixture position.\n" : "A concrete fixture argument.\n";
            foreach (char character in answer)
            {
                Console.Write(character);
                if (mode == "slow") await Task.Delay(25).ConfigureAwait(false);
            }
            return input.Length > 0 ? 0 : 18;
        }
    }
}
