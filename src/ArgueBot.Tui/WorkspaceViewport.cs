namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;

    internal sealed class WorkspaceViewport
    {
        private const int _MinimumWidth = 60;
        private const int _MinimumHeight = 20;
        private readonly TuiApplication _App;
        private readonly Func<string> _HistoryTitle;
        private volatile bool _TooSmall;
        internal WorkspaceViewport(TuiApplication app, Func<string> historyTitle)
        {
            _App = app; _HistoryTitle = historyTitle; app.RenderOverlay = Render;
            app.KeyFilter = key => _TooSmall && key.Code != KeyCode.F1 && key.Code != KeyCode.F2 && !(key.Code == KeyCode.Character && key.Rune == 'q' && (key.Modifiers & KeyModifiers.Ctrl) != 0);
        }
        private void Render(ISurface surface)
        {
            _TooSmall = surface.Size.Width < _MinimumWidth || surface.Size.Height < _MinimumHeight;
            _App.EnableMouseRouting = !_TooSmall; _App.ShowTooltips = !_TooSmall;
            if (!_TooSmall)
            {
                int divider = (int)(surface.Size.Width * 0.3) - 1;
                for (int line = 4; line < surface.Size.Height - 5; line++) surface.DrawStyledText(divider, line, Text.From("│", _App.Theme.Border));
                if (_App.CurrentFocusPath.Region == "threads") surface.DrawStyledText(1, 4, Text.From("› Conversations", _App.Theme.Accent));
                if (_App.CurrentFocusPath.Region == "transcript") new SurfaceView(surface, new Rect(divider + 2, 4, Math.Max(0, surface.Size.Width - divider - 3), 1)).DrawStyledText(0, 0, Text.From("› " + _HistoryTitle(), _App.Theme.Accent));
                if (_App.CurrentFocusPath.Region == "composer") surface.DrawStyledText(1, surface.Size.Height - 5, Text.From("› Note: Enter sends", _App.Theme.Accent));
                return;
            }
            surface.Fill(new Rect(0, 0, surface.Size.Width, surface.Size.Height), Cell.Blank(_App.Theme.Text));
            int width = System.Math.Max(1, surface.Size.Width - 4);
            int row = 1;
            foreach (string paragraph in new[] { "Terminal too small", "", "Resize to at least 60 columns by 20 rows to use the workspace.", "", "Press F1 for Help, F2 for Menu, or Ctrl+Q to detach. Dialogs remain available through the menu." })
            {
                foreach (StyledText line in TextWrapper.Wrap(Text.From(paragraph, _App.Theme.Text), width))
                {
                    if (row >= surface.Size.Height - 1) return;
                    surface.DrawStyledText(2, row++, line);
                }
                if (paragraph.Length == 0) row++;
            }
        }
    }
}
