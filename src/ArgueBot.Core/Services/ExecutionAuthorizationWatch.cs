namespace ArgueBot.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;

    internal sealed class ExecutionAuthorizationWatch : IAsyncDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly RequestContext _Context;
        private readonly CancellationTokenSource _Generation;
        private readonly CancellationTokenSource _Stop;
        private readonly TimeProvider _Clock;
        private readonly Task _Work;
        private readonly TaskCompletionSource<bool> _InitialCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<bool> InitialCheck { get { return _InitialCheck.Task; } }
        private string _FailureCode = "";
        internal string FailureCode { get { return Volatile.Read(ref _FailureCode); } }
        internal ExecutionAuthorizationWatch(DatabaseDriverBase database, RequestContext context, CancellationTokenSource generation, TimeProvider clock, CancellationToken token)
        {
            _Database = database; _Context = context; _Generation = generation; _Clock = clock;
            _Stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Work = WatchAsync();
        }
        private async Task WatchAsync()
        {
            try
            {
                while (true)
                {
                    if (!await _Database.Users.CanExecuteAsync(_Context, _Stop.Token).ConfigureAwait(false)) { Deny("authorization_revoked"); return; }
                    _InitialCheck.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromMilliseconds(200), _Clock, _Stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_Stop.IsCancellationRequested) { }
            catch (Exception exception) when (exception is not OutOfMemoryException) { Deny("authorization_unavailable"); }
        }
        private void Deny(string code) { Volatile.Write(ref _FailureCode, code); _InitialCheck.TrySetResult(false); _Generation.Cancel(); }
        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel();
            try { await _Work.ConfigureAwait(false); }
            finally { _Stop.Dispose(); }
        }
    }
}
