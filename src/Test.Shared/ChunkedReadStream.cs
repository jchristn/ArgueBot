namespace Test.Shared
{
    using System.IO;
    using System.Threading.Tasks;
    using System.Threading;
    using System;

    internal sealed class ChunkedReadStream : MemoryStream
    {
        private readonly int _Chunk;
        internal ChunkedReadStream(byte[] bytes, int chunk) : base(bytes, false) { _Chunk = chunk; }
        public override int Read(byte[] buffer, int offset, int count) { return base.Read(buffer, offset, Math.Min(count, _Chunk)); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { return base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, _Chunk)), token); }
    }
}
