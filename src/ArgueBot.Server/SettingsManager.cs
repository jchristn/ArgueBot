namespace ArgueBot.Server
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Services;

    internal sealed class SettingsManager : IDisposable
    {
        private readonly ArgueBotPaths _Paths;
        private readonly ServiceSettings _Running;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly string _StartedRevision;
        internal SettingsManager(ArgueBotPaths paths, ServiceSettings running) { _Paths = paths; _Running = running; _StartedRevision = Revision(); }
        internal async Task<SettingsView> ReadAsync(CancellationToken token)
        {
            ServiceSettings settings = await ServiceSettings.LoadAsync(_Paths, token).ConfigureAwait(false);
            string revision = Revision(); bool configured = settings.Database.ConnectionString.Length > 0;
            settings.Database.ConnectionString = "";
            return new SettingsView { Settings = settings, Revision = revision, RestartRequired = revision != _StartedRevision, EffectivePort = _Running.Port, PortOverridden = _Running.Port != settings.Port, ConnectionConfigured = configured };
        }
        internal async Task<SettingsView> SaveAsync(SettingsUpdate update, CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (update.Revision != Revision()) throw new DomainException(409, "revision_conflict", "Settings changed. Reload before saving.");
                ServiceSettings settings = await ServiceSettings.LoadAsync(_Paths, token).ConfigureAwait(false);
                if (update.TlsEnabled.HasValue) settings.TlsEnabled = update.TlsEnabled.Value;
                if (update.TlsPasswordEnvironmentVariable != null) settings.TlsPasswordEnvironmentVariable = update.TlsPasswordEnvironmentVariable;
                if (settings.TlsEnabled && !File.Exists(Path.Combine(_Paths.Home, "keys", "listener.pfx"))) throw new DomainException(400, "tls_certificate_missing", "Provision keys/listener.pfx before enabling HTTPS.");
                settings.Port = update.Port; settings.MaxConcurrentRuns = update.MaxConcurrentRuns; settings.AllowCli = update.AllowCli;
                if (update.StreamBatchCharacters.HasValue) settings.StreamBatchCharacters = update.StreamBatchCharacters.Value;
                if (update.StreamCommitMilliseconds.HasValue) settings.StreamCommitMilliseconds = update.StreamCommitMilliseconds.Value;
                if (update.EventRetentionEnabled.HasValue) settings.EventRetentionEnabled = update.EventRetentionEnabled.Value;
                if (update.EventRetentionDays.HasValue) settings.EventRetentionDays = update.EventRetentionDays.Value;
                if (update.EventMaxPerConversation.HasValue) settings.EventMaxPerConversation = update.EventMaxPerConversation.Value;
                if (update.EventMaintenanceSeconds.HasValue) settings.EventMaintenanceSeconds = update.EventMaintenanceSeconds.Value;
                if (update.EventRetentionBatchSize.HasValue) settings.EventRetentionBatchSize = update.EventRetentionBatchSize.Value;
                if (update.EventReplayPageSize.HasValue) settings.EventReplayPageSize = update.EventReplayPageSize.Value;
                if (update.HistoryPreviewCodePoints.HasValue) settings.HistoryPreviewCodePoints = update.HistoryPreviewCodePoints.Value;
                if (update.ContextMaxCharacters.HasValue) settings.ContextMaxCharacters = update.ContextMaxCharacters.Value;
                if (update.ContextMaxMessages.HasValue) settings.ContextMaxMessages = update.ContextMaxMessages.Value;
                settings.Database.Type = update.DatabaseType; settings.Database.ConnectionEnvironmentVariable = update.ConnectionEnvironmentVariable;
                await settings.SaveAsync(_Paths, token).ConfigureAwait(false);
                return await ReadAsync(token).ConfigureAwait(false);
            }
            finally { _Gate.Release(); }
        }
        private string Revision() { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_Paths.SettingsFile))); }
        public void Dispose() { _Gate.Dispose(); }
    }
}
