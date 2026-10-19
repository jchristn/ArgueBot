namespace ArgueBot.Core.Security
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using ArgueBot.Core.Configuration;

    /// <summary>AES-256-GCM session protection with per-token nonces and owner-only rotating keys outside the database.</summary>
    public sealed class TokenProtector
    {
        private readonly string _Directory;
        private readonly string _CurrentFile;
        /// <summary>Initialize a home-scoped key ring. Requires exclusive service ownership of the home.</summary>
        public TokenProtector(ArgueBotPaths paths)
        {
            _Directory = Path.Combine(paths.Home, "keys"); _CurrentFile = Path.Combine(_Directory, "session-current");
            Directory.CreateDirectory(_Directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!File.Exists(_CurrentFile)) Rotate();
        }
        /// <summary>Rotate the issuing key. Prior keys remain available until all their sessions expire.</summary>
        public string Rotate()
        {
            string keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            string keyFile = Path.Combine(_Directory, "session-" + keyId + ".key");
            byte[] key = RandomNumberGenerator.GetBytes(32);
            try { File.WriteAllBytes(keyFile, key); }
            finally { CryptographicOperations.ZeroMemory(key); }
            ProtectFile(keyFile);
            string temporary = _CurrentFile + ".tmp"; File.WriteAllText(temporary, keyId); ProtectFile(temporary); File.Move(temporary, _CurrentFile, true);
            return keyId;
        }
        /// <summary>Encrypt typed claims with authenticated ciphertext and a fresh random twelve-byte nonce.</summary>
        public string Protect(SessionClaims claims)
        {
            string keyId = File.ReadAllText(_CurrentFile).Trim(); byte[] key = ReadKey(keyId);
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(claims); byte[] nonce = RandomNumberGenerator.GetBytes(12); byte[] tag = new byte[16]; byte[] ciphertext = new byte[plaintext.Length];
            try
            {
                using (AesGcm aes = new AesGcm(key, 16)) { aes.Encrypt(nonce, plaintext, ciphertext, tag); }
                byte[] combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
                nonce.CopyTo(combined, 0); tag.CopyTo(combined, nonce.Length); ciphertext.CopyTo(combined, nonce.Length + tag.Length);
                return "v1." + keyId + "." + Convert.ToBase64String(combined).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
        }
        /// <summary>Verify and decrypt. Invalid, expired, oversized, or future-issued tokens return null.</summary>
        public SessionClaims? Unprotect(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 4096) return null;
            string[] pieces = token.Split('.'); if (pieces.Length != 3 || pieces[0] != "v1") return null;
            byte[]? key = null; byte[]? plaintext = null;
            try
            {
                key = ReadKey(pieces[1]);
                string encoded = pieces[2].Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                byte[] combined = Convert.FromBase64String(encoded); if (combined.Length < 28) return null;
                plaintext = new byte[combined.Length - 28];
                using (AesGcm aes = new AesGcm(key, 16)) { aes.Decrypt(combined.AsSpan(0, 12), combined.AsSpan(28), combined.AsSpan(12, 16), plaintext); }
                SessionClaims? claims = JsonSerializer.Deserialize<SessionClaims>(plaintext);
                if (claims == null || claims.ExpiresUtc <= DateTime.UtcNow || claims.IssuedUtc > DateTime.UtcNow.AddMinutes(1) || string.IsNullOrWhiteSpace(claims.SessionId) || string.IsNullOrWhiteSpace(claims.TenantId) || string.IsNullOrWhiteSpace(claims.UserId)) return null;
                return claims;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is IOException || ex is JsonException || ex is ArgumentException) { return null; }
            finally { if (key != null) CryptographicOperations.ZeroMemory(key); if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); }
        }
        private byte[] ReadKey(string keyId)
        {
            if (!Regex.IsMatch(keyId, "^[a-f0-9]{16}$", RegexOptions.CultureInvariant)) throw new FormatException("Invalid key identifier.");
            byte[] key = File.ReadAllBytes(Path.Combine(_Directory, "session-" + keyId + ".key"));
            if (key.Length != 32) { CryptographicOperations.ZeroMemory(key); throw new CryptographicException("Session key must contain exactly 32 bytes."); }
            return key;
        }
        private static void ProtectFile(string path) { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }
}
