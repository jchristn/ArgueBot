namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using Touchstone.Core;

    /// <summary>Cryptographic and server-side session lifecycle tests using disposable keys and databases.</summary>
    public static class SecuritySuites
    {
        /// <summary>Reusable security descriptors.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Security", "Session protection", new List<TestCaseDescriptor> { new TestCaseDescriptor("Security", "PasswordVerifiers", "Salted password verifiers reject replay and migrate schema-10 accounts without changing sessions", PasswordVerifierChecks.RunAsync), new TestCaseDescriptor("Security", "PermissionEvaluation", "Scoped permission truth table: explicit deny, write expansion, inheritance, ownership, administrator bypass and credential ceilings", AuthorizationChecks.RunAsync), new TestCaseDescriptor("Security", "ExecutionIdentity", "Accepted work survives session logout but current role/active state revokes model execution", ExecutionIdentityAsync), new TestCaseDescriptor("Security", "TlsTrust", "Local TLS pins bind certificate, home, endpoint, name and validity without trusting arbitrary certificates", TlsChecks.RunAsync), new TestCaseDescriptor("Security", "Identities", "Tenant identities, protected administration, password exchange, revision conflicts and immediate revocation", IdentitiesAsync), new TestCaseDescriptor("Security", "Audit", "Append-only audit inspection enforces explicit flags, tenant scope and anonymous accounting", AuditAsync), new TestCaseDescriptor("Security", "AdminBoundary", "Session privileges follow explicit flags and current active user and tenant state", AdminBoundaryAsync), new TestCaseDescriptor("Security", "Protection", "Random nonce, tamper rejection, expiry, key rotation and revocation", ProtectionAsync) }) }; }
        }
        private static async Task ExecutionIdentityAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-execution-identity-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await ExecutionIdentityChecks.RunAsync(database, paths, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task IdentitiesAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-identities-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await IdentityChecks.RunAsync(database, paths, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task AuditAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-audit-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await AuditChecks.RunAsync(database, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task AdminBoundaryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-flags-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    SessionMethods sessions = new SessionMethods(database, new TokenProtector(paths));
                    string session = await sessions.IssueLocalAsync(token).ConfigureAwait(false);
                    if (!(await sessions.AuthenticateAsync(session, token).ConfigureAwait(false))!.IsAdmin) throw new InvalidDataException("The seeded owner lost its explicit system administrator flag.");
                    using (SqliteConnection fixture = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
                    {
                        await fixture.OpenAsync(token).ConfigureAwait(false);
                        using (SqliteCommand command = fixture.CreateCommand())
                        {
                            command.CommandText = "UPDATE users SET is_admin=0,is_tenant_admin=1 WHERE id='usr_local';"; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            RequestContext tenantAdmin = await sessions.AuthenticateAsync(session, token).ConfigureAwait(false) ?? throw new InvalidDataException("Tenant administrator session was rejected.");
                            if (tenantAdmin.IsAdmin || !tenantAdmin.IsTenantAdmin || tenantAdmin.Role != "admin") throw new InvalidDataException("The admin role name escalated a tenant principal to system administrator.");
                            bool denied = false;
                            try { await database.Backups.EnumerateAsync(tenantAdmin, token: token).ConfigureAwait(false); }
                            catch (DomainException exception) when (exception.StatusCode == 403) { denied = true; }
                            if (!denied) throw new InvalidDataException("A tenant administrator could enumerate complete-service archives.");
                            command.CommandText = "UPDATE users SET active=0 WHERE id='usr_local';"; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            if (await sessions.AuthenticateAsync(session, token).ConfigureAwait(false) != null) throw new InvalidDataException("An inactive user retained its existing session.");
                            command.CommandText = "UPDATE users SET active=1 WHERE id='usr_local'; UPDATE tenants SET active=0 WHERE id='ten_local';"; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            if (await sessions.AuthenticateAsync(session, token).ConfigureAwait(false) != null) throw new InvalidDataException("An inactive tenant retained its existing session.");
                        }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
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
