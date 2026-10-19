namespace ArgueBot.Core.Security
{
    using System;
    using System.Security.Cryptography;

    internal sealed class PasswordVerifier
    {
        internal const int DefaultIterations = 600000;
        internal string Hash { get; }
        internal string Salt { get; }
        internal int Iterations { get; }
        private PasswordVerifier(string hash, string salt, int iterations) { Hash = hash; Salt = salt; Iterations = iterations; }
        internal static PasswordVerifier Create(string equivalent)
        {
            if (!Hex(equivalent, 64)) throw new ArgumentException("Supply SHA-256 password-equivalent material.", nameof(equivalent));
            byte[] material = Convert.FromHexString(equivalent); byte[] salt = RandomNumberGenerator.GetBytes(16); byte[]? hash = null;
            try
            {
                hash = Rfc2898DeriveBytes.Pbkdf2(material, salt, DefaultIterations, HashAlgorithmName.SHA256, 32);
                return new PasswordVerifier(Convert.ToHexString(hash), Convert.ToHexString(salt), DefaultIterations);
            }
            finally { CryptographicOperations.ZeroMemory(material); CryptographicOperations.ZeroMemory(salt); if (hash != null) CryptographicOperations.ZeroMemory(hash); }
        }
        internal static bool Matches(string equivalent, string storedHash, string storedSalt, int iterations)
        {
            bool inputValid = Hex(equivalent, 64); bool hashValid = Hex(storedHash, 64); bool saltValid = Hex(storedSalt, 32);
            bool workValid = iterations >= DefaultIterations && iterations <= 1000000;
            byte[] material = inputValid ? Convert.FromHexString(equivalent) : new byte[32];
            byte[] salt = saltValid ? Convert.FromHexString(storedSalt) : new byte[16];
            byte[] expected = hashValid ? Convert.FromHexString(storedHash) : new byte[32]; byte[]? actual = null;
            try
            {
                actual = Rfc2898DeriveBytes.Pbkdf2(material, salt, workValid ? iterations : DefaultIterations, HashAlgorithmName.SHA256, 32);
                bool matches = CryptographicOperations.FixedTimeEquals(actual, expected);
                return inputValid && hashValid && saltValid && workValid && matches;
            }
            finally { CryptographicOperations.ZeroMemory(material); CryptographicOperations.ZeroMemory(salt); CryptographicOperations.ZeroMemory(expected); if (actual != null) CryptographicOperations.ZeroMemory(actual); }
        }
        private static bool Hex(string? value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char character in value) if (!char.IsAsciiHexDigit(character)) return false;
            return true;
        }
    }
}
