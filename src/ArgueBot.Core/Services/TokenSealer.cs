namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Configuration;
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Encrypts bearer-token credentials and share-link tokens (AES-256-GCM, random nonce per value) so their owners can reveal them later.
    /// Authentication never uses these copies; it compares SHA-256 hashes. The key is <c>token.key</c> in the protected secrets directory.
    /// </summary>
    public sealed class TokenSealer
    {
        private static readonly byte[] Associated = Encoding.ASCII.GetBytes("arguebot-token-v1");
        private readonly byte[] _Key;

        /// <summary>Use an existing 32-byte key.</summary>
        public TokenSealer(byte[] key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (key.Length != 32) throw new ArgumentException("A token key must be 32 bytes.", nameof(key));
            _Key = (byte[])key.Clone();
        }

        /// <summary>Load <c>token.key</c> from <paramref name="directory"/>, creating it with owner-only permissions on first use.</summary>
        public static TokenSealer Open(string directory)
        {
            ArgumentNullException.ThrowIfNull(directory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "token.key");
            if (!File.Exists(path))
            {
                byte[] generated = RandomNumberGenerator.GetBytes(32);
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { HomePermissions.File(path); stream.Write(generated, 0, generated.Length); stream.Flush(true); }
                }
                catch (IOException) when (File.Exists(path)) { }
                finally { CryptographicOperations.ZeroMemory(generated); }
            }
            HomePermissions.File(path);
            byte[] key = File.ReadAllBytes(path);
            try
            {
                if (key.Length != 32) throw new InvalidDataException("The token key is invalid. Restore it from the protected secrets backup, or delete it so new tokens can be revealed (existing ones then cannot).");
                return new TokenSealer(key);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        /// <summary>Encrypt a token for storage.</summary>
        public string Seal(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            byte[] plaintext = Encoding.UTF8.GetBytes(token);
            byte[] envelope = new byte[12 + 16 + plaintext.Length];
            RandomNumberGenerator.Fill(envelope.AsSpan(0, 12));
            using (AesGcm cipher = new AesGcm(_Key, 16)) cipher.Encrypt(envelope.AsSpan(0, 12), plaintext, envelope.AsSpan(28), envelope.AsSpan(12, 16), Associated);
            CryptographicOperations.ZeroMemory(plaintext);
            return Convert.ToBase64String(envelope);
        }

        /// <summary>Decrypt a stored token, or null when it was sealed with another key or is damaged.</summary>
        public string? Unseal(string sealedToken)
        {
            ArgumentNullException.ThrowIfNull(sealedToken);
            byte[] envelope;
            try { envelope = Convert.FromBase64String(sealedToken); }
            catch (FormatException) { return null; }
            if (envelope.Length <= 28) return null;
            byte[] plaintext = new byte[envelope.Length - 28];
            try
            {
                using (AesGcm cipher = new AesGcm(_Key, 16)) cipher.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plaintext, Associated);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException) { return null; }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
}
