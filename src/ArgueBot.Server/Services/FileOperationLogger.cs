namespace ArgueBot.Server.Services
{
    using System;
    using System.Diagnostics;
    using Microsoft.Extensions.Logging;

    internal sealed class FileOperationLogger : ILogger
    {
        private readonly FileLogProvider _Provider;
        private readonly string _Category;
        internal FileOperationLogger(FileLogProvider provider, string category) { _Provider = provider; _Category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return null; }
        public bool IsEnabled(LogLevel logLevel) { return logLevel >= LogLevel.Information && logLevel != LogLevel.None; }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) _Provider.Write(_Category, logLevel, formatter(state, null), Activity.Current?.TraceId.ToString() ?? "", Activity.Current?.SpanId.ToString() ?? "");
        }
    }
}
