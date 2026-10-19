namespace ArgueBot.Tui
{
    using TUIKit;

    internal sealed class LayoutProbeSurface : ISurface
    {
        /// <inheritdoc />
        public Size Size { get; }
        internal LayoutProbeSurface(int width) { Size = new Size(width, 65536); }
        /// <inheritdoc />
        public void Set(int x, int y, Cell cell) { }
        /// <inheritdoc />
        public void Fill(Rect region, Cell cell) { }
    }
}
