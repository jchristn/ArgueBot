namespace ArgueBot.Tui
{
    using TUIKit;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal static class TerminalTheme
    {
        internal static Theme Dark
        {
            get
            {
                Theme source = Theme.Dark;
                Color black = Color.FromRgb(0, 0, 0);
                return new Theme("Dark", source.Text.WithBackground(black), source.Accent.WithBackground(black), source.Border.WithBackground(black), source.Muted.WithBackground(black), selection: source.Text.WithBackground(Color.FromRgb(48, 48, 48)));
            }
        }
        internal static Theme Inputs(Theme source)
        {
            Color background = source.Name == "Dark" ? Color.FromRgb(40, 40, 40) : Color.FromRgb(232, 232, 232);
            return new Theme(source.Name, source.Text.WithBackground(background), source.Accent.WithBackground(background), source.Border.WithBackground(background), source.Muted.WithBackground(background), selection: source.Selection);
        }
        internal static void ApplyInputs(Form form, Theme theme)
        {
            Theme inputs = Inputs(theme);
            for (int index = 0; index < form.FieldCount; index++)
            {
                IWidget field = form.GetField(index);
                if (field is TextField || field is Dropdown<string>) ThemeApplier.Apply(field, inputs);
            }
        }
    }
}
