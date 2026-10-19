namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;

    internal sealed class SplashScreen : Modal
    {
        private static readonly string[] _Lines = """
                                    _           _
          __ _ _ __ __ _ _   _  ___| |__   ___ | |_
         / _` | '__/ _` | | | |/ _ \ '_ \ / _ \| __|
        | (_| | | | (_| | |_| |  __/ |_) | (_) | |_
         \__,_|_|  \__, |\__,_|\___|_.__/ \___/ \__|
                   |___/
        """.Split('\n');
        private readonly CellStyle _Style;
        private SplashScreen(CellStyle style) { _Style = style; }
        private const string _Hint = "Press any key to continue (3 seconds)";

        internal static async Task ShowAsync(TuiApplication app, CancellationToken token)
        {
            SplashScreen splash = new SplashScreen(app.Theme.Text);
            using (CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task dismissal = DismissAsync(app, splash, timer.Token);
                try { await app.ShowAsync(splash).WaitAsync(token).ConfigureAwait(false); }
                finally
                {
                    timer.Cancel();
                    try { await dismissal.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (timer.IsCancellationRequested) { }
                }
            }
        }
        private static async Task DismissAsync(TuiApplication app, SplashScreen splash, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            app.Post(() => splash.RequestClose(null));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key) { Close(null); return true; }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = Math.Min(64, Math.Max(0, surface.Size.Width - 4));
            int maximumHeight = Math.Max(0, surface.Size.Height - 2);
            if (width < 8 || maximumHeight < 5) return;
            List<string> lines = new List<string>();
            bool fullLogo = width >= 52 && maximumHeight >= 21;
            if (fullLogo) lines.AddRange(_Lines);
            else lines.Add("arguebot");
            lines.Add("");
            string version = typeof(SplashScreen).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
            lines.Add("v" + version + " - (c) 2026 Joel Christner");
            lines.Add("MIT License");
            lines.Add("");
            lines.Add("Let your agents debate. Find better answers.");
            lines.Add("");
            lines.Add("https://github.com/jchristn/ArgueBot");
            lines.Add("");
            lines.Add("Ctrl+E: configure agents / harnesses | F1: help");
            int height = Math.Min(lines.Count + 6, maximumHeight);
            int left = (surface.Size.Width - width) / 2;
            int top = (surface.Size.Height - height) / 2;
            Rect box = new Rect(left, top, width, height);
            surface.Fill(box, Cell.Blank(_Style));
            surface.DrawBox(box, _Style.WithForeground(Color.FromPalette(6)), "arguebot");
            int logoWidth = 0;
            foreach (string line in _Lines) logoWidth = Math.Max(logoWidth, line.Length);
            for (int index = 0; index < lines.Count && index < height - 6; index++)
            {
                string line = lines[index];
                int available = width - 4;
                if (line.Length > available) line = line.Substring(0, available);
                int contentWidth = fullLogo && index < _Lines.Length ? logoWidth : line.Length;
                surface.DrawText(left + Math.Max(2, (width - contentWidth) / 2), top + 2 + index, line, _Style);
            }
            string hint = _Hint.Substring(0, Math.Min(_Hint.Length, width - 4));
            surface.DrawText(left + (width - hint.Length) / 2, top + height - 3, hint, _Style);
        }
    }
}
