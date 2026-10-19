namespace ArgueBot.Server
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Services;

    internal sealed class LocalEnrollment : IDisposable
    {
        private readonly string _Path;
        private readonly SessionMethods _Sessions;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private string _Code = "";
        private DateTime _Window = DateTime.UtcNow;
        private int _Requests;
        internal LocalEnrollment(ArgueBotPaths paths, SessionMethods sessions) { _Path = Path.Combine(paths.Home, "runtime", "pairing-code"); _Sessions = sessions; Rotate(); }
        internal async Task<string> PairAsync(string code, CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow - _Window > TimeSpan.FromMinutes(1)) { _Window = DateTime.UtcNow; _Requests = 0; }
                if (++_Requests > 20) throw new DomainException(429, "pairing_rate_limit", "Pairing attempt limit reached. Wait one minute before retrying.");
                byte[] supplied = Encoding.UTF8.GetBytes(code); byte[] expected = Encoding.UTF8.GetBytes(_Code);
                if (!CryptographicOperations.FixedTimeEquals(supplied, expected)) throw new DomainException(401, "invalid_pairing", "Pairing code is invalid or already used. Read the current local code.");
                string session = await _Sessions.IssueLocalAsync(token).ConfigureAwait(false); Rotate(); return session;
            }
            finally { _Gate.Release(); }
        }
        private void Rotate()
        {
            _Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            File.WriteAllText(_Path, _Code);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        public void Dispose() { _Gate.Dispose(); if (File.Exists(_Path)) File.Delete(_Path); }
    }
}
