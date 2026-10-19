namespace ArgueBot.Client
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Json;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;

    internal static class LocalTlsTrust
    {
        internal static HttpClientHandler CreateHandler(ArgueBotPaths paths)
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => Validate(paths, request, certificate, errors)
            };
        }
        private static bool Validate(ArgueBotPaths paths, HttpRequestMessage request, X509Certificate2? certificate, SslPolicyErrors errors)
        {
            if (certificate == null) return false;
            if (errors == SslPolicyErrors.None) return true;
            Uri? uri = request.RequestUri;
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "127.0.0.1" || certificate == null || (errors & ~(SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0 || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) return false;
            try
            {
                // Validate the IP SAN directly: .NET 8 on macOS can report a name mismatch for a matching IP SAN.
                if (!certificate.MatchesHostname(uri.Host, allowWildcards: false, allowCommonName: false)) return false;
                LocalServiceDescriptor? descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(File.ReadAllText(Path.Combine(paths.Home, "runtime", "service.json")));
                if (descriptor == null || descriptor.HomeId != LocalHomeIdentity.Read(paths) || descriptor.Endpoint != uri.GetLeftPart(UriPartial.Authority) || descriptor.CertificateSha256 == null || descriptor.CertificateSha256.Length != 64) return false;
                return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(descriptor.CertificateSha256), certificate.GetCertHash(HashAlgorithmName.SHA256));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is FormatException || exception is CryptographicException) { return false; }
        }
    }
}
