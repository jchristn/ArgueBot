namespace ArgueBot.Runners.Processes
{
    using ArgueBot.Core.Runners;
    using System.Text;

    internal sealed class ProcessOutputCapture
    {
        private readonly StringBuilder _Text = new StringBuilder();
        internal bool Truncated { get; private set; }
        internal void Append(string text)
        {
            _Text.Append(text);
            if (_Text.Length > RunnerFailureDiagnostics.CaptureCharacters)
            {
                _Text.Remove(0, _Text.Length - RunnerFailureDiagnostics.CaptureCharacters);
                Truncated = true;
            }
        }
        public override string ToString() { return _Text.ToString(); }
    }
}
