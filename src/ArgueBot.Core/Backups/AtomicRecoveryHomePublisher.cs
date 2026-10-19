namespace ArgueBot.Core.Backups
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class AtomicRecoveryHomePublisher : IRecoveryHomePublisher
    {
        /// <inheritdoc />
        public Task PublishAsync(string stagingHome, string destinationHome, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Directory.Move(stagingHome, destinationHome); return Task.CompletedTask;
        }
    }
}
