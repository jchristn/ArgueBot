namespace Test.Shared
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Backups;

    internal sealed class FailingRecoveryPublisher : IRecoveryHomePublisher
    {
        /// <inheritdoc />
        public Task PublishAsync(string stagingHome, string destinationHome, CancellationToken token = default)
        { throw new IOException("Fixture publication failure after database commit."); }
    }
}
