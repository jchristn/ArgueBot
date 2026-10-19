namespace ArgueBot.Server.Services
{
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using Microsoft.Extensions.Logging;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;

    internal sealed class EventMaintenance : IAsyncDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ServiceSettings _Settings;
        private readonly ILogger _Logger;
        private readonly CancellationTokenSource _Stop;
        private readonly Task _Work;
        internal EventMaintenance(DatabaseDriverBase database, ServiceSettings settings, ILogger logger, CancellationToken token)
        {
            _Database = database; _Settings = settings; _Logger = logger;
            _Stop = CancellationTokenSource.CreateLinkedTokenSource(token, database.OwnershipToken);
            _Work = settings.EventRetentionEnabled ? Task.Run(RunAsync, CancellationToken.None) : Task.CompletedTask;
        }
        private async Task RunAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                try
                {
                    long removed = await _Database.PruneEventsAsync(DateTime.UtcNow.AddDays(-_Settings.EventRetentionDays), _Settings.EventMaxPerConversation, _Settings.EventRetentionBatchSize, _Stop.Token).ConfigureAwait(false);
                    if (removed != 0) _Logger.LogInformation("Replay retention removed {Count} events", removed);
                }
                catch (OperationCanceledException) when (_Stop.IsCancellationRequested) { return; }
                catch (Exception exception) when (exception is not OutOfMemoryException) { _Logger.LogWarning("Replay retention failed {FailureType}", exception.GetType().Name); }
                try { await Task.Delay(TimeSpan.FromSeconds(_Settings.EventMaintenanceSeconds), _Stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_Stop.IsCancellationRequested) { return; }
            }
        }
        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel();
            try { await _Work.ConfigureAwait(false); }
            finally { _Stop.Dispose(); }
        }
    }
}
