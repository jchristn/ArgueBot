namespace ArgueBot.Tests.Fixtures
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "plain";
            string input = await Console.In.ReadToEndAsync().ConfigureAwait(false);
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
