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
                settings.Port = update.Port; settings.MaxConcurrentRuns = update.MaxConcurrentRuns; settings.AllowCli = update.AllowCli;
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
