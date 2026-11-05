namespace ArgueBot.Core.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Salted PBKDF2-SHA256 password verifiers, applied to the SHA-256 of the password so the format matches
    /// verifiers created by earlier schema versions. Comparison is constant-time.
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>PBKDF2 iterations for new verifiers.</summary>
        public const int Iterations = 600000;

        /// <summary>Hash a password. Returns the verifier, salt and iteration count through out parameters.</summary>
        public static void Create(string password, out string hash, out string salt, out int iterations)
        {
            ArgumentNullException.ThrowIfNull(password);
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            try
            {
                hash = Derive(password, saltBytes, Iterations);
                salt = Convert.ToHexString(saltBytes);
                iterations = Iterations;
            }
            finally { CryptographicOperations.ZeroMemory(saltBytes); }
        }

        /// <summary>True when the password matches the stored verifier. Empty verifiers never match.</summary>
        public static bool Verify(string password, string hash, string salt, int iterations)
        {
            ArgumentNullException.ThrowIfNull(password);
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt) || iterations < 1) return false;
            byte[] expected;
            byte[] saltBytes;
            try { expected = Convert.FromHexString(hash); saltBytes = Convert.FromHexString(salt); }
            catch (FormatException) { return false; }
            byte[] actual = Convert.FromHexString(Derive(password, saltBytes, iterations));
            try { return CryptographicOperations.FixedTimeEquals(actual, expected); }
            finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(expected); CryptographicOperations.ZeroMemory(saltBytes); }
        }

        private static string Derive(string password, byte[] salt, int iterations)
        {
            byte[] material = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            byte[]? derived = null;
            try
            {
                derived = Rfc2898DeriveBytes.Pbkdf2(material, salt, iterations, HashAlgorithmName.SHA256, 32);
                return Convert.ToHexString(derived);
            }
            finally { CryptographicOperations.ZeroMemory(material); if (derived != null) CryptographicOperations.ZeroMemory(derived); }
        }
    }
}
