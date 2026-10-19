namespace ArgueBot.Core.Services
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;

    internal sealed class MessageStreamWriter : IAsyncDisposable
    {
        private readonly DatabaseDriverBase _Database;
        private readonly RequestContext _Context;
        private readonly Message _Message;
        private readonly int _Characters;
        private readonly TimeSpan _Interval;
        private readonly TimeProvider _Clock;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly CancellationTokenSource _Fault = new CancellationTokenSource();
        private readonly StringBuilder _Pending = new StringBuilder();
        private readonly StringBuilder _Content;
        private readonly Task _Timer;
        private int _Failed;
        internal CancellationToken FaultToken { get { return _Fault.Token; } }
        internal bool Failed { get { return Volatile.Read(ref _Failed) != 0; } }
        internal MessageStreamWriter(DatabaseDriverBase database, RequestContext context, Message message, int characters, TimeSpan interval, TimeProvider clock)
        {
            _Database = database; _Context = context; _Message = message; _Characters = characters; _Interval = interval; _Clock = clock;
            _Content = new StringBuilder(message.Content); _Timer = TickAsync();
        }
        internal async Task AppendAsync(string delta, CancellationToken token)
        {
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Failed) throw new DomainException(503, "stream_persistence_failed", "Stream persistence failed. The run requires explicit recovery.");
                _Pending.Append(delta); _Content.Append(delta);
                if (_Pending.Length >= _Characters) await FlushAsync(token).ConfigureAwait(false);
            }
            finally { _Gate.Release(); }
        }
        private async Task TickAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(_Interval, _Clock, _Stop.Token).ConfigureAwait(false);
                    await _Gate.WaitAsync(_Stop.Token).ConfigureAwait(false);
                    try { await FlushAsync(_Stop.Token).ConfigureAwait(false); }
                    finally { _Gate.Release(); }
                }
            }
            catch (OperationCanceledException) when (_Stop.IsCancellationRequested) { }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { Interlocked.Exchange(ref _Failed, 1); _Fault.Cancel(); }
        }
        private async Task FlushAsync(CancellationToken token, bool final = false)
        {
            if (_Pending.Length == 0) return;
            int characters = _Pending.Length;
            if (char.IsHighSurrogate(_Pending[characters - 1]))
            {
                if (final) throw new DomainException(502, "invalid_runner_text", "The runner ended with an incomplete Unicode character.");
                characters--;
            }
            if (characters == 0) return;
            string delta = _Pending.ToString(0, characters);
            try { new UTF8Encoding(false, true).GetByteCount(delta); }
            catch (EncoderFallbackException) { throw new DomainException(502, "invalid_runner_text", "The runner emitted invalid Unicode text."); }
            _Message.Content = _Content.ToString(0, _Content.Length - (_Pending.Length - characters));
            await _Database.UpdateMessageAsync(_Context, _Message, delta, token).ConfigureAwait(false);
            _Pending.Remove(0, characters);
        }
        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel(); await _Timer.ConfigureAwait(false);
            await _Gate.WaitAsync().ConfigureAwait(false);
            try { await FlushAsync(CancellationToken.None, true).ConfigureAwait(false); }
            finally { _Gate.Release(); _Gate.Dispose(); _Stop.Dispose(); _Fault.Dispose(); }
        }
    }
}
