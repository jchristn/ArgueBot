namespace ArgueBot.Server.Services
{
    using System;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using ArgueBot.Core.Configuration;

    internal static class ListenerCertificate
    {
        internal static X509Certificate2? Load(ArgueBotPaths paths, ServiceSettings settings)
        {
            if (!settings.TlsEnabled) return null;
            string? password = settings.TlsPasswordEnvironmentVariable.Length == 0 ? null : Environment.GetEnvironmentVariable(settings.TlsPasswordEnvironmentVariable) ?? throw new InvalidOperationException("The listener TLS password environment reference is not set.");
            string file = Path.Combine(paths.Home, "keys", "listener.pfx");
            if (new FileInfo(file).Length > 1048576) throw new InvalidDataException("The listener certificate exceeds one MiB.");
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(file, password, OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet);
            if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            { certificate.Dispose(); throw new InvalidDataException("The listener certificate requires a valid private key and validity period."); }
            HomePermissions.File(file);
            return certificate;
        }
    }
}
