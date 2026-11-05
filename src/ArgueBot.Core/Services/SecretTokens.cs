namespace ArgueBot.Core.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>Random bearer tokens and their stored SHA-256 verifiers.</summary>
    public static class SecretTokens
    {
        /// <summary>A random URL-safe token with a recognizable prefix, such as abt_ for credentials.</summary>
        public static string Create(string prefix, int bytes = 30)
        {
            ArgumentNullException.ThrowIfNull(prefix);
            byte[] random = RandomNumberGenerator.GetBytes(bytes);
            try { return prefix + Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
            finally { CryptographicOperations.ZeroMemory(random); }
        }

        /// <summary>Lowercase hex SHA-256 of a token, used for lookup and storage.</summary>
        public static string Hash(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        }

        /// <summary>The last four characters of a token, for display.</summary>
        public static string Last4(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            return token.Length <= 4 ? token : token.Substring(token.Length - 4);
        }
    }
}
