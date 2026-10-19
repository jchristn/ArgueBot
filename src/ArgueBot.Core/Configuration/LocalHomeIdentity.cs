namespace ArgueBot.Core.Configuration
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;

    /// <summary>Local process binding backed by a private random key and normalized absolute home path. Routing identifiers and challenge proofs are not user credentials.</summary>
    public static class LocalHomeIdentity
    {
        /// <summary>Read an existing home binding without creating directories or files. Returns null when its private key is absent.</summary>
        public static string? Read(ArgueBotPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            byte[]? seed = ReadSeed(paths);
            if (seed == null) return null;
            try { return Bind(paths, seed); }
            finally { CryptographicOperations.ZeroMemory(seed); }
        }
        private static byte[]? ReadSeed(ArgueBotPaths paths)
        {
            string file = Path.Combine(paths.Home, "runtime", "home-identity");
            if (!File.Exists(file)) return null;
            byte[] seed = File.ReadAllBytes(file);
            if (seed.Length != 64) { CryptographicOperations.ZeroMemory(seed); throw new InvalidDataException("The application home identity file is invalid."); }
            try { foreach (byte value in seed) Hex(value); return seed; }
            catch { CryptographicOperations.ZeroMemory(seed); throw; }
        }
        private static int Hex(byte value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            throw new InvalidDataException("The application home identity file is invalid.");
        }
        private static string Bind(ArgueBotPaths paths, byte[] seed)
        {
            string home = Path.TrimEndingDirectorySeparator(paths.Home);
            if (OperatingSystem.IsWindows()) home = home.ToUpperInvariant();
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            { hash.AppendData(seed); hash.AppendData(Encoding.UTF8.GetBytes("\n" + home)); return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(); }
        }
        /// <summary>Prove possession of this home's private key for a 64-hex-character random challenge and the actual listener port. The message is protocol- and audience-bound.</summary>
        public static string CreateProof(ArgueBotPaths paths, int port, string challenge)
        {
            ArgumentNullException.ThrowIfNull(paths); ArgumentNullException.ThrowIfNull(challenge);
            if (port < 1 || port > 65535 || challenge.Length != 64) throw new ArgumentException("Use a valid listener port and a 32-byte hex challenge.");
            foreach (char value in challenge)
                if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F'))) throw new ArgumentException("Use a hex challenge.");
            byte[] seed = ReadSeed(paths) ?? throw new InvalidOperationException("The home identity key is not initialized.");
            byte[] key = new byte[32];
            try
            {
                for (int index = 0; index < key.Length; index++) key[index] = (byte)((Hex(seed[index * 2]) << 4) | Hex(seed[index * 2 + 1]));
                string message = "ArgueBot local service protocol 1\n" + Bind(paths, seed) + "\n" + port.ToString(CultureInfo.InvariantCulture) + "\n" + challenge.ToUpperInvariant();
                return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(seed); }
        }
        /// <summary>Compare a listener proof in constant time. A fresh random challenge must be used for every connection attempt.</summary>
        public static bool VerifyProof(ArgueBotPaths paths, int port, string challenge, string proof)
        {
            ArgumentNullException.ThrowIfNull(proof);
            if (proof.Length != 64) return false;
            try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(CreateProof(paths, port, challenge)), Convert.FromHexString(proof)); }
            catch (FormatException) { return false; }
        }
        /// <summary>Atomically create an owner-readable random key if missing, then return its path-bound routing identifier. Concurrent clients share the winning key.</summary>
        public static string Ensure(ArgueBotPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths); paths.EnsureDirectories();
            string file = Path.Combine(paths.Home, "runtime", "home-identity");
            string lockFile = file + ".lock";
            FileStream? lease = null; long started = Stopwatch.GetTimestamp();
            while (lease == null)
            {
                try { lease = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException exception)
                {
                    if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5)) throw new IOException("The local home identity is busy or unavailable.", exception);
                    Thread.Sleep(10);
                }
            }
            using (lease)
            {
                HomePermissions.File(lockFile);
                if (File.Exists(file)) { HomePermissions.File(file); return Read(paths)!; }
                string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    FileStreamOptions options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    using (FileStream output = new FileStream(temporary, options))
                    {
                        HomePermissions.File(temporary);
                        byte[] key = RandomNumberGenerator.GetBytes(32); byte[] seed = new byte[64];
                        try
                        {
                            const string alphabet = "0123456789ABCDEF";
                            for (int index = 0; index < key.Length; index++) { seed[index * 2] = (byte)alphabet[key[index] >> 4]; seed[index * 2 + 1] = (byte)alphabet[key[index] & 15]; }
                            output.Write(seed); output.Flush(true);
                        }
                        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(seed); }
                    }
                    try { File.Move(temporary, file, false); }
                    catch (IOException) when (File.Exists(file)) { }
                    return Read(paths) ?? throw new IOException("The application home identity could not be published.");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }
}
