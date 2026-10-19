namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>System-administrator recovery inventory and durable administrative operation boundary.</summary>
    public interface IBackupMethods
    {
        /// <summary>Create a pending archive record and audit its administrator initiator.</summary>
        Task<BackupEntry> CreateAsync(RequestContext context, string name, bool encrypted, bool includesSecrets, CancellationToken token = default);
        /// <summary>Filter and page global recovery metadata. Archive contents are not read by this operation.</summary>
        Task<BackupPage> EnumerateAsync(RequestContext context, string search = "", int page = 1, int size = 25, CancellationToken token = default);
        /// <summary>Read global metadata using a system-administrator authorization decision.</summary>
        Task<BackupEntry> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Publish archive size/checksum/status after its atomic filesystem publication, or mark a failed artifact.</summary>
        Task SetStatusAsync(RequestContext context, string id, string status, long bytes = 0, string digest = "", CancellationToken token = default);
        /// <summary>Accept a durable create, verify or restore operation without persisting its passphrase.</summary>
        Task<BackupOperation> CreateOperationAsync(RequestContext context, string backupId, string kind, string targetHome = "", int port = 0, CancellationToken token = default);
        /// <summary>Page durable administrative recovery jobs, optionally filtered to one artifact.</summary>
        Task<BackupOperationPage> EnumerateOperationsAsync(RequestContext context, string backupId = "", int page = 1, int size = 25, CancellationToken token = default);
        /// <summary>Read a durable operation and the names of missing credential references.</summary>
        Task<BackupOperation> ReadOperationAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Checkpoint a service-owned recovery task and its nonsecret credential diagnostics.</summary>
        Task SaveOperationAsync(RequestContext context, BackupOperation operation, CancellationToken token = default);
        /// <summary>Refuse file mutation while an accepted recovery operation still owns the artifact. The service serializes acceptance and deletion around this check.</summary>
        Task EnsureIdleAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Delete an artifact's metadata after its file has been staged for removal. In-flight operations prevent deletion.</summary>
        Task DeleteAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Service startup reconciliation only: never automatically replay interrupted administrative operations.</summary>
        Task ReconcileAsync(CancellationToken token = default);
    }
}
