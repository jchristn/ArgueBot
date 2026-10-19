namespace ArgueBot.Server
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal sealed class PasswordExchange : IDisposable
    {
        private readonly SessionMethods _Sessions;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private long _Window = Stopwatch.GetTimestamp();
        private int _Requests;
        internal PasswordExchange(SessionMethods sessions) { _Sessions = sessions; }
        internal async Task<string> LoginAsync(LoginRequest request, string correlationId, CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Stopwatch.GetElapsedTime(_Window) >= TimeSpan.FromMinutes(1)) { _Window = Stopwatch.GetTimestamp(); _Requests = 0; }
                if (++_Requests > 20) throw new DomainException(429, "login_rate_limit", "Login attempt limit reached. Wait one minute before retrying.");
                return await _Sessions.LoginAsync(request, correlationId, token).ConfigureAwait(false);
            }
            finally { _Gate.Release(); }
        }
        public void Dispose() { _Gate.Dispose(); }
    }
}
