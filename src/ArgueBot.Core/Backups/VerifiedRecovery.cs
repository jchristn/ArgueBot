namespace ArgueBot.Core.Backups
{
    using System.Collections.Generic;
    using System.IO;
    using System;
    using ArgueBot.Core.Configuration;

    /// <summary>A fully verified private staging directory, removed when its owner disposes it.</summary>
    public sealed class VerifiedRecovery : IDisposable
    {
        /// <summary>Private staging path. Callers must never serve this directory directly.</summary>
        public string DirectoryPath { get; }
        /// <summary>Verified archive manifest.</summary>
        public RecoveryManifest Manifest { get; }
        /// <summary>Verified source configuration, potentially sensitive for explicitly secret-inclusive archives.</summary>
        public ServiceSettings Settings { get; }
        /// <summary>Whether authenticated archive encryption was used.</summary>
        public bool Encrypted { get; }
        /// <summary>Credential reference names missing from the current environment; never their values.</summary>
        public IReadOnlyList<string> MissingReferences { get; }
        internal VerifiedRecovery(string directory, RecoveryManifest manifest, ServiceSettings settings, bool encrypted, IReadOnlyList<string> missing)
        { DirectoryPath = directory; Manifest = manifest; Settings = settings; Encrypted = encrypted; MissingReferences = missing; }
        /// <summary>Remove all decrypted staging content.</summary>
        public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }
}
