namespace ArgueBot.Tui
{
    using TUIKit;
    using TUIKit.Theming;

    internal sealed class ConversationListSurface : ISurface
    {
        private readonly ISurface _Surface;
        private readonly Theme _Theme;
        internal ConversationListSurface(ISurface surface, Theme theme) { _Surface = surface; _Theme = theme; }
        /// <inheritdoc />
        public Size Size { get { return _Surface.Size; } }
        /// <inheritdoc />
        public void Set(int x, int y, Cell cell) { _Surface.Set(x, y, Restyle(cell)); }
        /// <inheritdoc />
        public void Fill(Rect region, Cell cell) { _Surface.Fill(region, Restyle(cell)); }
        private Cell Restyle(Cell cell)
        {
            if (cell.Style.Background.Equals(_Theme.Accent.Foreground))
                return cell.IsContinuation ? Cell.Continuation(_Theme.Selection) : Cell.Glyph(cell.Grapheme, _Theme.Selection, cell.Width);
            return cell;
        }
    }
}
