namespace ArgueBot.Core.Backups
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Validate a logical backup without opening or modifying any application database.</summary>
    public static class SnapshotVerifier
    {
        /// <summary>Check format, known schema, ordering, row shapes, counts, completeness and SHA-256 integrity.</summary>
        public static Task ValidateAsync(string path, CancellationToken token = default)
        { return LogicalSnapshot.ValidateAsync(path, token); }
    }
}
