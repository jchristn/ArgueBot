namespace ArgueBot.Core.Backups
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Final new-home publication boundary. Implementations must atomically move the complete staging directory to an absent destination, never overwrite an existing home, and honor cancellation before publication.</summary>
    public interface IRecoveryHomePublisher
    {
        /// <summary>Publish reconciled, closed storage. A failure after external database commit causes protected staging retention for explicit operator recovery.</summary>
        Task PublishAsync(string stagingHome, string destinationHome, CancellationToken token = default);
    }
}
