namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Configuration;
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Encrypts session tokens with AES-256-GCM under a per-home key and a random 96-bit nonce per token.
    /// The token is <c>abs_</c> followed by base64url(nonce, tag, ciphertext). Tampered, truncated or foreign tokens fail closed.
    /// </summary>
    public sealed class SessionProtector
    {
        /// <summary>Prefix that identifies a session token, as opposed to a credential bearer token.</summary>
        public const string Prefix = "abs_";
        private static readonly byte[] Associated = Encoding.ASCII.GetBytes("arguebot-session-v1");
        private readonly byte[] _Key;

        /// <summary>Use an existing 32-byte key.</summary>
        public SessionProtector(byte[] key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (key.Length != 32) throw new ArgumentException("A session key must be 32 bytes.", nameof(key));
            _Key = (byte[])key.Clone();
        }

        /// <summary>Load the session key from <paramref name="directory"/>, creating it with owner-only permissions on first use.</summary>
        public static SessionProtector Open(string directory)
        {
            ArgumentNullException.ThrowIfNull(directory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "session.key");
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
                if (key.Length != 32) throw new InvalidDataException("The session key is invalid. Delete " + path + " to sign everyone out and create a new one.");
                return new SessionProtector(key);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        internal string Protect(SessionTokenPayload payload)
        {
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
            byte[] envelope = new byte[12 + 16 + plaintext.Length];
            RandomNumberGenerator.Fill(envelope.AsSpan(0, 12));
            using (AesGcm cipher = new AesGcm(_Key, 16)) cipher.Encrypt(envelope.AsSpan(0, 12), plaintext, envelope.AsSpan(28), envelope.AsSpan(12, 16), Associated);
            CryptographicOperations.ZeroMemory(plaintext);
            return Prefix + Base64Url(envelope);
        }

        internal SessionTokenPayload? Unprotect(string token)
        {
            if (token == null || !token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length > 2048) return null;
            byte[] envelope;
            try { envelope = FromBase64Url(token.Substring(Prefix.Length)); }
            catch (FormatException) { return null; }
            if (envelope.Length <= 28) return null;
            byte[] plaintext = new byte[envelope.Length - 28];
            try
            {
                using (AesGcm cipher = new AesGcm(_Key, 16)) cipher.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plaintext, Associated);
                return JsonSerializer.Deserialize<SessionTokenPayload>(plaintext);
            }
            catch (CryptographicException) { return null; }
            catch (JsonException) { return null; }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }

        private static string Base64Url(byte[] value) { return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static byte[] FromBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
        }
    }
}
