namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class RunnerDiagnosticStore
    {
        private readonly string _Directory;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        internal RunnerDiagnosticStore(string directory) { _Directory = directory; }
        internal async Task SaveAsync(GenerationAttempt attempt, Exception exception, CancellationToken token)
        {
            if (exception.Data[RunnerFailureDiagnostics.ExceptionDataKey] is not RunnerFailureDiagnostics diagnostics) return;
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_Directory);
                HomePermissions.Directory(_Directory);
                // Keep at most 100 bounded captures, including the new capture.
                foreach (FileInfo old in new DirectoryInfo(_Directory).GetFiles("att_*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(99)) old.Delete();
                string path = Path.Combine(_Directory, attempt.Id + ".json");
                string json = JsonSerializer.Serialize(new { attempt.Id, attempt.ConversationId, attempt.RunId, attempt.MessageId, CapturedUtc = DateTime.UtcNow, ErrorCode = exception is DomainException domain ? domain.Code : exception.GetType().Name, Diagnostics = diagnostics });
                string temporary = path + ".tmp";
                try
                {
                    using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        HomePermissions.File(temporary);
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                        await stream.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
                        stream.Flush(true);
                    }
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            finally { _Gate.Release(); }
        }
    }
}
