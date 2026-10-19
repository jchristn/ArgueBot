namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Security;
    using Touchstone.Core;

    /// <summary>Cryptographic and server-side session lifecycle tests using disposable keys and databases.</summary>
    public static class SecuritySuites
    {
        /// <summary>Reusable security descriptors.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Security", "Session protection", new List<TestCaseDescriptor> { new TestCaseDescriptor("Security", "Protection", "Random nonce, tamper rejection, expiry, key rotation and revocation", ProtectionAsync) }) }; }
        }
        private static async Task ProtectionAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-security-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); paths.EnsureDirectories();
                TokenProtector protector = new TokenProtector(paths);
                SessionClaims claims = new SessionClaims { SessionId = "ses_test", TenantId = "ten_test", UserId = "usr_test", IssuedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddMinutes(30), TokenId = "nonce" };
                string first = protector.Protect(claims); string second = protector.Protect(claims);
                if (first == second || protector.Unprotect(first)?.TenantId != claims.TenantId) throw new InvalidDataException("Session nonce or decryption failed.");
                char[] altered = first.ToCharArray(); int index = altered.Length - 10; altered[index] = altered[index] == 'A' ? 'B' : 'A';
                if (protector.Unprotect(new string(altered)) != null) throw new InvalidDataException("Altered token was accepted.");
                protector.Rotate();
                if (protector.Unprotect(first) == null) throw new InvalidDataException("Key rotation invalidated a still-valid session unexpectedly.");
                claims.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
                if (protector.Unprotect(protector.Protect(claims)) != null) throw new InvalidDataException("Expired token was accepted.");
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    SessionMethods sessions = new SessionMethods(database, protector);
                    string session = await sessions.IssueLocalAsync(token).ConfigureAwait(false);
                    if (await sessions.AuthenticateAsync(session, token).ConfigureAwait(false) == null) throw new InvalidDataException("Issued local session was rejected.");
                    await sessions.RevokeAsync(session, token).ConfigureAwait(false);
                    if (await sessions.AuthenticateAsync(session, token).ConfigureAwait(false) != null) throw new InvalidDataException("Revoked session remains valid.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
