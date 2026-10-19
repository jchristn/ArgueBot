namespace ArgueBot.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal sealed class BackupManager : IAsyncDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ArgueBotPaths _Paths;
        private readonly CancellationTokenSource _Shutdown = new CancellationTokenSource();
        private readonly SemaphoreSlim _Execution = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _Acceptance = new SemaphoreSlim(1, 1);
        private readonly List<Task> _Tasks = new List<Task>();
        private readonly IReadOnlyList<DependencyReference> _Dependencies;
        private bool _Disposed;
        internal BackupManager(DatabaseDriverBase database, ArgueBotPaths paths)
        {
            _Database = database; _Paths = paths;
            using (Stream stream = typeof(BackupManager).Assembly.GetManifestResourceStream("ArgueBot.dependencies.json") ?? throw new InvalidOperationException("Dependency inventory is missing."))
            { _Dependencies = JsonSerializer.Deserialize<List<DependencyReference>>(stream, Api.Json) ?? throw new InvalidDataException("Dependency inventory is invalid."); }
        }
        internal async Task InitializeAsync(CancellationToken token)
        {
            await _Database.Backups.ReconcileAsync(token).ConfigureAwait(false);
            RequestContext system = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            foreach (string staged in Directory.EnumerateFiles(Path.Combine(_Paths.Home, "backups"), "bak_*.arguebak.deleting"))
            {
                string id = Path.GetFileName(staged).Replace(".arguebak.deleting", "", StringComparison.Ordinal);
                string published = ArtifactPath(id);
                try
                {
                    await _Database.Backups.ReadAsync(system, id, token).ConfigureAwait(false);
                    if (File.Exists(published)) throw new IOException("Recovery deletion staging conflicts with an existing archive.");
                    File.Move(staged, published, false);
                }
                catch (DomainException exception) when (exception.StatusCode == 404) { File.Delete(staged); }
            }
            int page = 1;
            while (true)
            {
                BackupPage entries = await _Database.Backups.EnumerateAsync(system, page: page++, size: 100, token: token).ConfigureAwait(false);
                foreach (BackupEntry entry in entries.Items)
                    if (entry.Status == "ready" && !File.Exists(ArtifactPath(entry.Id))) await _Database.Backups.SetStatusAsync(system, entry.Id, "unavailable", token: token).ConfigureAwait(false);
                if ((long)entries.PageNumber * entries.PageSize >= entries.TotalCount) break;
            }
        }
        internal string ArtifactPath(string id)
        {
            if (!Regex.IsMatch(id, "^bak_[A-Za-z0-9_-]{1,60}$", RegexOptions.CultureInvariant)) throw new DomainException(404, "not_found", "Recovery artifact not found.");
            return Path.Combine(_Paths.Home, "backups", id + ".arguebak");
        }
        internal async Task<BackupOperation> AcceptAsync(RequestContext context, string kind, string? id, BackupRequest request, CancellationToken token)
        {
            if (!context.IsAdmin) throw new DomainException(403, "forbidden", "Complete-service recovery requires a system administrator.");
            ArgumentNullException.ThrowIfNull(request);
            string? password = string.IsNullOrEmpty(request.Password) ? null : request.Password;
            if (password != null && (password.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(password) > 1024)) throw new DomainException(400, "invalid_passphrase", "Use a passphrase from twelve characters to 1024 UTF-8 bytes.");
            if (kind == "create" && request.IncludeSecrets && password == null) throw new DomainException(400, "encryption_required", "Secret-inclusive backups require a passphrase.");
            string destination = "";
            if (kind == "restore")
            {
                if (!Regex.IsMatch(request.DestinationName ?? "", "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)) throw new DomainException(400, "invalid_destination", "Use a new home name of one to 64 letters, digits, hyphens or underscores.");
                if (request.Port != 0 && (request.Port < 1024 || request.Port > 65535)) throw new DomainException(400, "invalid_port", "Use a listener port from 1024 to 65535.");
                if (!Enum.IsDefined(request.RestoreDatabaseType)) throw new DomainException(400, "invalid_provider", "Select SQLite or PostgreSQL for recovery.");
                if (request.RestoreDatabaseType == DatabaseTypeEnum.Postgresql)
                {
                    if (!Regex.IsMatch(request.RestoreConnectionEnvironmentVariable, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable(request.RestoreConnectionEnvironmentVariable))) throw new DomainException(400, "missing_target_configuration", "Set a PostgreSQL connection environment reference on the service machine before restoring.");
                }
                else if (request.RestoreConnectionEnvironmentVariable.Length > 0) throw new DomainException(400, "unused_target_reference", "SQLite recovery does not use a PostgreSQL environment reference.");
                destination = Path.Combine(_Paths.Home, "restores", request.DestinationName!);
                if (Directory.Exists(destination) || File.Exists(destination)) throw new DomainException(409, "destination_exists", "Choose a new recovery home name.");
            }
            await _Acceptance.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_Disposed, this);
                _Tasks.RemoveAll(task => task.IsCompleted);
                if (_Tasks.Count >= 4) throw new DomainException(429, "recovery_capacity", "Wait for a recovery operation to finish before submitting another.");
                BackupEntry entry = kind == "create" ? await _Database.Backups.CreateAsync(context, request.Name, password != null, request.IncludeSecrets, token).ConfigureAwait(false) : await _Database.Backups.ReadAsync(context, id ?? "", token).ConfigureAwait(false);
                if (kind != "create" && (entry.Status != "ready" || !File.Exists(ArtifactPath(entry.Id)))) throw new DomainException(409, "backup_unavailable", "This recovery archive is unavailable.");
                BackupOperation operation = await _Database.Backups.CreateOperationAsync(context, entry.Id, kind, destination, kind == "restore" ? request.Port : 0, kind == "restore" ? request.RestoreDatabaseType.ToString() : "", kind == "restore" ? request.RestoreConnectionEnvironmentVariable : "", token).ConfigureAwait(false);
                bool includeSecrets = request.IncludeSecrets;
                _Tasks.Add(Task.Run(() => ExecuteAsync(context, operation, includeSecrets, password)));
                request.Password = "";
                return operation;
            }
            finally { _Acceptance.Release(); }
        }
        private async Task ExecuteAsync(RequestContext context, BackupOperation operation, bool includeSecrets, string? password)
        {
            bool entered = false;
            try
            {
                await _Execution.WaitAsync(_Shutdown.Token).ConfigureAwait(false); entered = true;
                operation.State = "running"; await _Database.Backups.SaveOperationAsync(context, operation, _Shutdown.Token).ConfigureAwait(false);
                string file = ArtifactPath(operation.BackupId);
                if (operation.Kind == "create")
                {
                    await RecoveryArchive.CreateAsync(_Database, _Paths, file, includeSecrets, password, _Dependencies, token: _Shutdown.Token).ConfigureAwait(false);
                    string digest;
                    using (FileStream stream = File.OpenRead(file)) { digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, _Shutdown.Token).ConfigureAwait(false)).ToLowerInvariant(); }
                    await _Database.Backups.SetStatusAsync(context, operation.BackupId, "ready", new FileInfo(file).Length, digest, _Shutdown.Token).ConfigureAwait(false);
                }
                else
                {
                    using (VerifiedRecovery recovery = await RecoveryArchive.OpenAsync(file, password, token: _Shutdown.Token).ConfigureAwait(false))
                    {
                        operation.MissingReferences = recovery.MissingReferences.ToList();
                        if (operation.Kind == "restore")
                        {
                            int? port = operation.Port == 0 ? null : operation.Port;
                            if (operation.TargetProvider == DatabaseTypeEnum.Postgresql.ToString())
                            {
                                DatabaseSettings destination = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, ConnectionEnvironmentVariable = operation.TargetConnectionEnvironmentVariable };
                                RecoveryRestoreResult restored = await RecoveryRestorer.ToNewPostgresqlHomeAsync(recovery, operation.TargetHome, destination, port, token: _Shutdown.Token).ConfigureAwait(false);
                                operation.Port = restored.Port;
                            }
                            else { RecoveryRestoreResult restored = await RecoveryRestorer.ToNewHomeAsync(recovery, operation.TargetHome, port, _Shutdown.Token).ConfigureAwait(false); operation.Port = restored.Port; }
                        }
                    }
                }
                operation.State = "completed";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                operation.State = "failed";
                if (exception is RecoveryPublicationException publication) operation.RetainedStagingHome = publication.StagingHome;
                operation.ErrorCode = exception is RecoveryPublicationException ? "restore_publication_failed" : exception is DomainException domain ? domain.Code : exception is OperationCanceledException ? "service_interrupted" : exception is CryptographicException ? "archive_authentication_failed" : exception is InvalidDataException ? "archive_invalid" : exception is IOException ? "recovery_io_failed" : "recovery_failed";
                if (operation.Kind == "create") await _Database.Backups.SetStatusAsync(context, operation.BackupId, "failed", token: CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                password = null;
                try { await _Database.Backups.SaveOperationAsync(context, operation, CancellationToken.None).ConfigureAwait(false); }
                finally { if (entered) _Execution.Release(); }
            }
        }
        internal async Task DeleteAsync(RequestContext context, string id, CancellationToken token)
        {
            await _Acceptance.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _Database.Backups.EnsureIdleAsync(context, id, token).ConfigureAwait(false);
                string file = ArtifactPath(id); string staged = file + ".deleting";
                if (File.Exists(file)) File.Move(file, staged, false);
                try { await _Database.Backups.DeleteAsync(context, id, token).ConfigureAwait(false); }
                catch { if (File.Exists(staged)) File.Move(staged, file, false); throw; }
                if (File.Exists(staged)) File.Delete(staged);
            }
            finally { _Acceptance.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            await _Acceptance.WaitAsync().ConfigureAwait(false);
            try { _Disposed = true; _Shutdown.Cancel(); }
            finally { _Acceptance.Release(); }
            await Task.WhenAll(_Tasks).ConfigureAwait(false);
            _Shutdown.Dispose(); _Execution.Dispose(); _Acceptance.Dispose();
        }
    }
}
