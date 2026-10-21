namespace ArgueBot.Core.Database
{
    using System;
    using System.Security.Cryptography;

    internal sealed class LegacyPasswordVerifier
    {
        internal const int DefaultIterations = 600000;
        internal string Hash { get; }
        internal string Salt { get; }
        internal int Iterations { get; }
        private LegacyPasswordVerifier(string hash, string salt, int iterations) { Hash = hash; Salt = salt; Iterations = iterations; }
        internal static LegacyPasswordVerifier Create(string equivalent)
        {
            if (!Hex(equivalent, 64)) throw new ArgumentException("Supply SHA-256 password-equivalent material.", nameof(equivalent));
            byte[] material = Convert.FromHexString(equivalent); byte[] salt = RandomNumberGenerator.GetBytes(16); byte[]? hash = null;
            try
            {
                hash = Rfc2898DeriveBytes.Pbkdf2(material, salt, DefaultIterations, HashAlgorithmName.SHA256, 32);
                return new LegacyPasswordVerifier(Convert.ToHexString(hash), Convert.ToHexString(salt), DefaultIterations);
            }
            finally { CryptographicOperations.ZeroMemory(material); CryptographicOperations.ZeroMemory(salt); if (hash != null) CryptographicOperations.ZeroMemory(hash); }
        }
        private static bool Hex(string? value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char character in value) if (!char.IsAsciiHexDigit(character)) return false;
            return true;
        }
    }
}
