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
                Color background = Color.FromRgb(0x17, 0x19, 0x1C);
                CellStyle text = new CellStyle(Color.FromRgb(0xE3, 0xE5, 0xE8), background);
                CellStyle accent = new CellStyle(Color.FromRgb(0x8A, 0xAD, 0xD8), background);
                CellStyle muted = new CellStyle(Color.FromRgb(0xA1, 0xA7, 0xB0), background);
                return new Theme("Dark", text, accent, new CellStyle(Color.FromRgb(0x39, 0x3F, 0x47), background), muted,
                    success: new CellStyle(Color.FromRgb(0x94, 0xB6, 0xA0), background),
                    warning: new CellStyle(Color.FromRgb(0xD4, 0xAD, 0x72), background),
                    error: new CellStyle(Color.FromRgb(0xDA, 0x94, 0x94), background),
                    info: muted, selection: text.WithBackground(Color.FromRgb(0x30, 0x3D, 0x50)));
            }
        }
        internal static Theme Inputs(Theme source)
        {
            Color background = source.Name == "Dark" ? Color.FromRgb(0x25, 0x28, 0x2D) : Color.FromRgb(232, 232, 232);
            return new Theme(source.Name, source.Text.WithBackground(background), source.Accent.WithBackground(background), source.Border.WithBackground(background), source.Muted.WithBackground(background), success: source.Success.WithBackground(background), warning: source.Warning.WithBackground(background), error: source.Error.WithBackground(background), info: source.Info.WithBackground(background), selection: source.Selection);
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
