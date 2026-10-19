namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;

    internal static class TlsChecks
    {
        internal static Task RunAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string home = Path.Combine(Path.GetTempPath(), "arguebot-tls-trust-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); paths.EnsureDirectories();
                using (RSA key = RSA.Create(2048))
                {
                    CertificateRequest issuer = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    SubjectAlternativeNameBuilder names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); issuer.CertificateExtensions.Add(names.Build());
                    using (X509Certificate2 certificate = issuer.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)))
                    using (HttpClientHandler handler = LocalServiceConnector.CreateHttpHandler(home))
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1:4531/health"))
                    {
                        LocalServiceDescriptor descriptor = new LocalServiceDescriptor { HomeId = LocalHomeIdentity.Ensure(paths), Endpoint = "https://127.0.0.1:4531", CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256) };
                        string file = Path.Combine(home, "runtime", "service.json");
                        File.WriteAllText(file, JsonSerializer.Serialize(descriptor)); HomePermissions.File(file);
                        Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> validate = handler.ServerCertificateCustomValidationCallback ?? throw new InvalidDataException("No local TLS policy was configured.");
                        if (!validate(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors) || handler.AllowAutoRedirect) throw new InvalidDataException("Exact local pinning or redirect refusal failed.");
                        if (!validate(request, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("Managed IP SAN verification did not handle the macOS .NET 8 platform name error.");
                        CertificateRequest foreignIssuer = new CertificateRequest("CN=127.0.0.2", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                        SubjectAlternativeNameBuilder foreignNames = new SubjectAlternativeNameBuilder(); foreignNames.AddIpAddress(IPAddress.Parse("127.0.0.2")); foreignIssuer.CertificateExtensions.Add(foreignNames.Build());
                        using (X509Certificate2 foreign = foreignIssuer.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)))
                        {
                            descriptor.CertificateSha256 = foreign.GetCertHashString(HashAlgorithmName.SHA256); File.WriteAllText(file, JsonSerializer.Serialize(descriptor));
                            if (validate(request, foreign, null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("An exact pin bypassed the actual certificate IP SAN mismatch.");
                        }
                        descriptor.CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256); File.WriteAllText(file, JsonSerializer.Serialize(descriptor));
                        using (HttpRequestMessage different = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1:4532/health"))
                        { if (validate(different, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("A local certificate pin crossed listener ports."); }
                        descriptor.CertificateSha256 = new string('0', 64); File.WriteAllText(file, JsonSerializer.Serialize(descriptor));
                        if (validate(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("A mismatched certificate was trusted.");
                        descriptor.CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256); descriptor.HomeId = "foreign"; File.WriteAllText(file, JsonSerializer.Serialize(descriptor));
                        if (validate(request, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("A foreign home descriptor granted TLS trust.");
                        using (X509Certificate2 expired = issuer.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1)))
                        {
                            descriptor.HomeId = LocalHomeIdentity.Read(paths) ?? ""; descriptor.CertificateSha256 = expired.GetCertHashString(HashAlgorithmName.SHA256); File.WriteAllText(file, JsonSerializer.Serialize(descriptor));
                            if (validate(request, expired, null, SslPolicyErrors.RemoteCertificateChainErrors)) throw new InvalidDataException("A matching pin bypassed certificate expiry.");
                        }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
            return Task.CompletedTask;
        }
    }
}
