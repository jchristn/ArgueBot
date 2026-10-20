namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal abstract class HelpDialog : DialogModal
    {
        private readonly ReadOnlyTextView _Help = new ReadOnlyTextView
        {
            Text = "Using this dialog\n\nMenus: click an option, or use Up/Down and Enter. The scrollbar reveals more options.\n\nForms: Tab/Shift+Tab moves between fields and buttons. Agent and settings forms also accept Ctrl+S to save.\n\nDropdowns: Enter opens the list; arrows choose; Enter confirms. Escape closes an open dropdown before leaving the dialog.\n\nDebate setup: use Back/Next or Alt+Left/Right. Review the setup before choosing Start debate. Back retains your answers.\n\nLong text: arrows, PageUp/PageDown, Home/End and the mouse wheel scroll. The scrollbar shows more text.\n\nEscape returns one level. Cancel leaves saved data unchanged. Confirmations default to Cancel."
        };
        private readonly ButtonRow _Back = new ButtonRow();
        private bool _ShowingHelp;
        private bool _BackFocused;
        private string? _PreviousFooter;
        protected bool HelpIsOpen { get { return _ShowingHelp; } }
        protected HelpDialog() { _Back.Add("Back to dialog", CloseHelp).TooltipText = "Return to the dialog with your draft unchanged."; }
        private void CloseHelp() { _ShowingHelp = false; _Back.OnFocusChanged(false); _Help.OnFocusChanged(false); FooterHint = _PreviousFooter; }
        protected bool HandleHelp(KeyEvent key)
        {
            if (key.Code == KeyCode.F1)
            {
                if (_ShowingHelp) CloseHelp();
                else { _PreviousFooter = FooterHint; FooterHint = "F1/Esc: return to dialog"; _ShowingHelp = true; _BackFocused = false; _Back.OnFocusChanged(false); _Help.Text = _Help.Text; _Help.OnFocusChanged(true); }
                return true;
            }
            if (!_ShowingHelp) return false;
            if (key.Code == KeyCode.Escape) { CloseHelp(); return true; }
            if (key.Code == KeyCode.Tab) { _BackFocused = !_BackFocused; _Back.OnFocusChanged(_BackFocused); _Help.OnFocusChanged(!_BackFocused); }
            else if (_BackFocused) _Back.HandleKey(key); else _Help.HandleKey(key);
            return true;
        }
        protected bool HandleHelpMouse(MouseEvent mouse)
        {
            if (!_ShowingHelp) return false;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return true;
            int y = mouse.Y - ContentBounds.Y; int buttonRow = Math.Max(0, ContentBounds.Height - 1);
            MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, y >= buttonRow ? 0 : y, mouse.Modifiers, mouse.ClickCount);
            if (y >= buttonRow) _Back.HandleMouse(local); else _Help.HandleMouse(local);
            return true;
        }
        protected void SetFooter(string hint) { if (_ShowingHelp) _PreviousFooter = hint; else FooterHint = hint; }
        protected void ApplyHelpTheme(Theme theme) { _Help.ApplyTheme(theme); _Back.ApplyTheme(theme); }
        protected int HelpHeight(int normalHeight) { return _ShowingHelp ? Math.Max(18, normalHeight) : normalHeight; }
        protected bool RenderHelp(ISurface content)
        {
            if (!_ShowingHelp) return false;
            int buttonRow = Math.Max(0, content.Size.Height - 1);
            _Help.Render(new SurfaceView(content, new Rect(0, 0, content.Size.Width, Math.Max(0, buttonRow - 1))));
            _Back.Render(new SurfaceView(content, new Rect(0, buttonRow, content.Size.Width, 1)));
            return true;
        }
    }
}
