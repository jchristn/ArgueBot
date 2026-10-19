namespace ArgueBot.Server.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using Microsoft.Extensions.Logging;

    internal sealed class FileLogProvider : ILoggerProvider
    {
        private readonly object _Gate = new object();
        private readonly string _Path;
        internal FileLogProvider(string directory)
        {
            Directory.CreateDirectory(directory);
            _Path = Path.Combine(directory, "operations.jsonl");
        }
        public ILogger CreateLogger(string categoryName) { return new FileOperationLogger(this, categoryName); }
        internal void Write(string category, LogLevel level, string message, string traceId, string spanId)
        {
            lock (_Gate)
            {
                try
                {
                    if (File.Exists(_Path) && new FileInfo(_Path).Length >= 5242880)
                    {
                        for (int index = 4; index >= 1; index--)
                        {
                            string previous = index == 1 ? _Path : _Path + "." + (index - 1);
                            if (File.Exists(previous)) File.Move(previous, _Path + "." + index, true);
                        }
                    }
                    string record = JsonSerializer.Serialize(new { timestampUtc = DateTime.UtcNow, category, level = level.ToString(), message, traceId, spanId });
                    using (FileStream stream = new FileStream(_Path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        writer.WriteLine(record);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        public void Dispose() { }
    }
}
