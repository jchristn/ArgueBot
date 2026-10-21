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
