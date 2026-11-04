namespace ArgueBot.Runners.Mcp
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class McpFrames
    {
        internal static async Task<string?> ReadAsync(StreamReader reader, CancellationToken token)
        {
            StringBuilder value = new StringBuilder(); char[] character = new char[1];
            while (await reader.ReadAsync(character.AsMemory(), token).ConfigureAwait(false) != 0)
            {
                if (character[0] == '\n') return value.ToString();
                if (value.Length >= 131072) throw new InvalidDataException("MCP frame exceeds the invocation limit.");
                if (character[0] != '\r') value.Append(character[0]);
            }
            return value.Length == 0 ? null : throw new InvalidDataException("MCP frame ended before its delimiter.");
        }
    }
}
