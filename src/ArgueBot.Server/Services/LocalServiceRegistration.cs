namespace ArgueBot.Server.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;

    internal sealed class LocalServiceRegistration : IDisposable
    {
        private readonly string _Path;
        private readonly string _InstanceId = Guid.NewGuid().ToString("N");
        internal LocalServiceRegistration(ArgueBotPaths paths, int port, bool tls, string certificateSha256)
        {
            _Path = Path.Combine(paths.Home, "runtime", "service.json");
            LocalServiceDescriptor descriptor = new LocalServiceDescriptor { HomeId = LocalHomeIdentity.Ensure(paths), Endpoint = (tls ? "https" : "http") + "://127.0.0.1:" + port, CertificateSha256 = certificateSha256, ProcessId = Environment.ProcessId, InstanceId = _InstanceId, StartedUtc = DateTime.UtcNow };
            string temporary = _Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(descriptor));
            HomePermissions.File(temporary);
            File.Move(temporary, _Path, true);
        }
        public void Dispose()
        {
            try
            {
                if (File.Exists(_Path) && JsonSerializer.Deserialize<LocalServiceDescriptor>(File.ReadAllText(_Path))?.InstanceId == _InstanceId) File.Delete(_Path);
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }
}
