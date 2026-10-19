namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using ArgueBot.Core.Configuration;

    internal static class CertificateFixture
    {
        internal static void Create(ArgueBotPaths paths)
        {
            string directory = Path.Combine(paths.Home, "keys"); Directory.CreateDirectory(directory); HomePermissions.Directory(directory);
            using (RSA key = RSA.Create(2048))
            {
                CertificateRequest request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                SubjectAlternativeNameBuilder names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                using (X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2)))
                {
                    string file = Path.Combine(directory, "listener.pfx");
                    File.WriteAllBytes(file, certificate.Export(X509ContentType.Pfx)); HomePermissions.File(file);
                }
            }
        }
    }
}
