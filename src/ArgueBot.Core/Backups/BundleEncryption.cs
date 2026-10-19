namespace ArgueBot.Core.Backups
{
    using System.Buffers.Binary;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading;
    using System;

    /// <summary>Bounded-memory, authenticated recovery archive encryption with a terminal authenticated frame.</summary>
    public static class BundleEncryption
    {
        private const int _ChunkSize = 65536;
        private const int _Iterations = 600000;
        private static readonly byte[] _Magic = Encoding.ASCII.GetBytes("ARGBK001");
        /// <summary>Encrypt an archive with a fresh salt and nonce prefix. The destination stream remains open.</summary>
        public static async Task EncryptAsync(Stream source, Stream destination, string password, CancellationToken token = default)
        {
            ValidatePassword(password);
            byte[] header = new byte[44]; _Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8, 4), 1);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(12, 4), _Iterations);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), _ChunkSize);
            RandomNumberGenerator.Fill(header.AsSpan(20, 24));
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(20, 16), _Iterations, HashAlgorithmName.SHA256, 32);
            byte[] plaintext = new byte[_ChunkSize]; byte[] ciphertext = new byte[_ChunkSize]; byte[] tag = new byte[16]; byte[] length = new byte[4]; uint index = 0;
            try
            {
                await destination.WriteAsync(header.AsMemory(), token).ConfigureAwait(false);
                using (AesGcm cipher = new AesGcm(key, 16))
                {
                    while (true)
                    {
                        int count = await source.ReadAsync(plaintext.AsMemory(), token).ConfigureAwait(false);
                        BinaryPrimitives.WriteInt32BigEndian(length, count);
                        cipher.Encrypt(Nonce(header, index), plaintext.AsSpan(0, count), ciphertext.AsSpan(0, count), tag, AssociatedData(header, index, count));
                        await destination.WriteAsync(length.AsMemory(), token).ConfigureAwait(false);
                        await destination.WriteAsync(ciphertext.AsMemory(0, count), token).ConfigureAwait(false);
                        await destination.WriteAsync(tag.AsMemory(), token).ConfigureAwait(false);
                        if (count == 0) break;
                        index = checked(index + 1);
                    }
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
        }
        /// <summary>Decrypt only authenticated frames, rejecting wrong passwords, corruption, reordering and truncation.</summary>
        public static async Task DecryptAsync(Stream source, Stream destination, string password, long maximumBytes = 10737418240, CancellationToken token = default)
        {
            ValidatePassword(password);
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            byte[] header = new byte[44]; await source.ReadExactlyAsync(header.AsMemory(), token).ConfigureAwait(false);
            if (!header.AsSpan(0, 8).SequenceEqual(_Magic) || BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4)) != 1 || BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12, 4)) != _Iterations || BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)) != _ChunkSize) throw new InvalidDataException("Unsupported encrypted recovery format.");
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(20, 16), _Iterations, HashAlgorithmName.SHA256, 32);
            byte[] plaintext = new byte[_ChunkSize]; byte[] ciphertext = new byte[_ChunkSize]; byte[] tag = new byte[16]; byte[] length = new byte[4]; uint index = 0; long total = 0;
            try
            {
                using (AesGcm cipher = new AesGcm(key, 16))
                {
                    while (true)
                    {
                        await source.ReadExactlyAsync(length.AsMemory(), token).ConfigureAwait(false);
                        int count = BinaryPrimitives.ReadInt32BigEndian(length);
                        if (count < 0 || count > _ChunkSize || count > maximumBytes - total) throw new InvalidDataException("Encrypted recovery size exceeds its configured limit.");
                        await source.ReadExactlyAsync(ciphertext.AsMemory(0, count), token).ConfigureAwait(false);
                        await source.ReadExactlyAsync(tag.AsMemory(), token).ConfigureAwait(false);
                        cipher.Decrypt(Nonce(header, index), ciphertext.AsSpan(0, count), tag, plaintext.AsSpan(0, count), AssociatedData(header, index, count));
                        if (count == 0)
                        {
                            if (await source.ReadAsync(length.AsMemory(0, 1), token).ConfigureAwait(false) != 0) throw new InvalidDataException("Encrypted recovery has trailing data.");
                            break;
                        }
                        await destination.WriteAsync(plaintext.AsMemory(0, count), token).ConfigureAwait(false); total += count;
                        index = checked(index + 1);
                    }
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
        }
        /// <summary>Test the encrypted archive magic without retaining or advancing the caller's stream position.</summary>
        public static bool IsEncrypted(Stream stream)
        {
            if (!stream.CanSeek) throw new ArgumentException("A seekable archive stream is required.", nameof(stream));
            long position = stream.Position; byte[] magic = new byte[8]; int read = stream.Read(magic, 0, magic.Length); stream.Position = position;
            return read == magic.Length && magic.AsSpan().SequenceEqual(_Magic);
        }
        private static byte[] Nonce(byte[] header, uint index)
        {
            byte[] nonce = new byte[12]; header.AsSpan(36, 8).CopyTo(nonce);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), index); return nonce;
        }
        private static byte[] AssociatedData(byte[] header, uint index, int length)
        {
            byte[] associated = new byte[52]; header.CopyTo(associated, 0);
            BinaryPrimitives.WriteUInt32BigEndian(associated.AsSpan(44, 4), index);
            BinaryPrimitives.WriteInt32BigEndian(associated.AsSpan(48, 4), length); return associated;
        }
        private static void ValidatePassword(string password)
        {
            if (password == null || password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 1024) throw new ArgumentException("Use a recovery passphrase of at least twelve characters and at most 1024 UTF-8 bytes.", nameof(password));
        }
    }
}
