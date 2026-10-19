namespace ArgueBot.Client
{
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Services;

    internal static class EventFrameReader
    {
        internal static async IAsyncEnumerable<EventFrame> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken token)
        {
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, true))
            {
                char[] buffer = new char[4096]; StringBuilder pending = new StringBuilder(); bool carriageReturn = false;
                while (true)
                {
                    int count;
                    using (CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        idle.CancelAfter(TimeSpan.FromSeconds(ServiceProtocol.EventIdleSeconds));
                        try { count = await reader.ReadAsync(buffer.AsMemory(), idle.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException(504, "event_stream_timeout", "The event stream stopped sending heartbeats."); }
                        catch (DecoderFallbackException) { throw new DomainException(502, "invalid_event", "The event stream contains invalid UTF-8."); }
                    }
                    if (count == 0) yield break;
                    for (int index = 0; index < count; index++)
                    {
                        char value = buffer[index];
                        if (carriageReturn && value == '\n') { carriageReturn = false; continue; }
                        carriageReturn = value == '\r';
                        pending.Append(carriageReturn ? '\n' : value);
                        if (pending.Length > ServiceProtocol.MaximumEventFrameCharacters) throw new DomainException(502, "event_frame_too_large", "An event frame exceeded the protocol limit.");
                        if (pending.Length >= 2 && pending[pending.Length - 1] == '\n' && pending[pending.Length - 2] == '\n')
                        {
                            string name = ""; string id = ""; List<string> data = new List<string>();
                            foreach (string line in pending.ToString().Split('\n'))
                            {
                                if (line.StartsWith("event:", StringComparison.Ordinal)) name = line.Substring(6).Trim();
                                else if (line.StartsWith("id:", StringComparison.Ordinal)) id = line.Substring(3).Trim();
                                else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line.Substring(5).TrimStart());
                            }
                            pending.Clear(); yield return new EventFrame(name, id, string.Join("\n", data));
                        }
                    }
                }
            }
        }
    }
}
