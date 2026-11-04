namespace ArgueBot.Tui
{
    using System;
    using TUIKit.Widgets;

    internal sealed class CatalogEditor<T>
    {
        internal Form Form { get; } = new Form();
        internal Func<T> Extract { get; set; } = () => throw new InvalidOperationException("Editor not initialized.");
    }
}
