namespace ArgueBot.Tui
{
    using System;
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
        private const string _Hint = "Press any key to continue (3 seconds)";

        internal static async Task ShowAsync(TuiApplication app, CancellationToken token)
        {
            SplashScreen splash = new SplashScreen();
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
            int width = Math.Min(56, Math.Max(0, surface.Size.Width - 4));
            int height = Math.Min(12, Math.Max(0, surface.Size.Height - 2));
            if (width < 4 || height < 3) return;
            int left = (surface.Size.Width - width) / 2;
            int top = (surface.Size.Height - height) / 2;
            Rect box = new Rect(left, top, width, height);
            CellStyle style = CellStyle.Default;
            surface.Fill(box, Cell.Blank(style));
            surface.DrawBox(box, style.WithForeground(Color.FromPalette(6)), "arguebot");
            int logoWidth = 0;
            foreach (string line in _Lines) logoWidth = Math.Max(logoWidth, line.Length);
            for (int index = 0; index < _Lines.Length && index < height - 4; index++)
            {
                string line = _Lines[index];
                int available = Math.Max(0, width - 4);
                if (line.Length > available) line = line.Substring(0, available);
                surface.DrawText(left + Math.Max(2, (width - logoWidth) / 2), top + 2 + index, line, style);
            }
            string hint = _Hint.Substring(0, Math.Min(_Hint.Length, width - 4));
            surface.DrawText(left + (width - hint.Length) / 2, top + height - 2, hint, style);
        }
    }
}
