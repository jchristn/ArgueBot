namespace ArgueBot.Tui
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit.Hosting;

    internal sealed class UiDispatcher
    {
        private readonly TuiApplication _Application;
        private readonly CancellationToken _Token;
        internal UiDispatcher(TuiApplication application, CancellationToken token) { _Application = application; _Token = token; }
        internal async Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (CancellationTokenRegistration cancellation = _Token.Register(() => completion.TrySetCanceled(_Token)))
            {
                _Application.Post(async () =>
                {
                    if (_Token.IsCancellationRequested) return;
                    try { completion.TrySetResult(await action().ConfigureAwait(false)); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                });
                return await completion.Task.ConfigureAwait(false);
            }
        }
    }
}
