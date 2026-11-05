namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class SecretTextFilter
    {
        private readonly List<string> _Secrets;
        private readonly Func<string, CancellationToken, Task> _Forward;
        private string _Pending = "";
        internal SecretTextFilter(IEnumerable<string> secrets, Func<string, CancellationToken, Task> forward)
        { _Secrets = secrets.Where(value => value.Length > 0).Distinct().OrderByDescending(value => value.Length).ToList(); _Forward = forward; }
        internal string Scrub(string text)
        {
            foreach (string secret in _Secrets) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal).Replace(Uri.EscapeDataString(secret), "[redacted]", StringComparison.Ordinal);
            return text;
        }
        internal async Task WriteAsync(string text, CancellationToken token)
        {
            _Pending = Scrub(_Pending + text);
            int retain = 0;
            foreach (string secret in _Secrets)
            {
                foreach (string candidate in new[] { secret, Uri.EscapeDataString(secret) })
                    for (int length = Math.Min(candidate.Length - 1, _Pending.Length); length > retain; length--)
                        if (_Pending.AsSpan(_Pending.Length - length).SequenceEqual(candidate.AsSpan(0, length))) { retain = length; break; }
            }
            int count = _Pending.Length - retain;
            if (count > 0) { string ready = _Pending.Substring(0, count); _Pending = _Pending.Substring(count); await _Forward(ready, token).ConfigureAwait(false); }
        }
        internal async Task CompleteAsync(CancellationToken token)
        { if (_Pending.Length > 0) await _Forward(Scrub(_Pending), token).ConfigureAwait(false); _Pending = ""; }
    }
}
