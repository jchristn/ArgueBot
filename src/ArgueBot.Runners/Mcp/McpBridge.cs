namespace ArgueBot.Runners.Mcp
{
    using System;
    using System.IO;
    using System.IO.Pipes;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Process-bound MCP stdio relay; no database or open-workspace management access.</summary>
    public static class McpBridge
    {
        /// <summary>Relay bounded newline-delimited MCP messages to the current invocation's owner-only pipe.</summary>
        public static async Task RunAsync(string pipeName, Stream input, Stream output, CancellationToken token = default)
        {
            if (!Regex.IsMatch(pipeName, "^abm-[a-f0-9]{32}$")) throw new ArgumentException("Invalid invocation pipe.", nameof(pipeName));
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                await pipe.ConnectAsync(10000, token).ConfigureAwait(false);
                Task upstream = CopyAsync(input, pipe, lifetime.Token); Task downstream = CopyAsync(pipe, output, lifetime.Token);
                await Task.WhenAny(upstream, downstream).ConfigureAwait(false); lifetime.Cancel(); pipe.Close();
                try { await Task.WhenAll(upstream, downstream).ConfigureAwait(false); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (IOException) when (lifetime.IsCancellationRequested) { }
            }
        }
        private static async Task CopyAsync(Stream source, Stream destination, CancellationToken token)
        {
            using (StreamReader reader = new StreamReader(source, new UTF8Encoding(false, true), false, 4096, true))
            using (StreamWriter writer = new StreamWriter(destination, new UTF8Encoding(false), 4096, true))
            {
                while (await McpFrames.ReadAsync(reader, token).ConfigureAwait(false) is string frame)
                { await writer.WriteLineAsync(frame.AsMemory(), token).ConfigureAwait(false); await writer.FlushAsync(token).ConfigureAwait(false); }
            }
        }
    }
}
