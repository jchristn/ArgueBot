namespace ArgueBot.Tui
{
    using System.Threading.Tasks;
    using TUIKit.Hosting;

    internal static class TerminalDialogs
    {
        internal static async Task<string?> PromptStyledAsync(this TuiApplication app, string title, string initial = "")
        {
            TextPromptDialog dialog = new TextPromptDialog(title, initial); dialog.ApplyTheme(app.Theme);
            return await app.ShowAsync<string>(dialog).ConfigureAwait(false);
        }
        internal static async Task<int> SelectStyledAsync(this TuiApplication app, string title, params string[] choices)
        {
            ChoiceDialog dialog = new ChoiceDialog(title, choices); dialog.ApplyTheme(app.Theme);
            object? result = await app.ShowAsync(dialog).ConfigureAwait(false);
            return result is int index ? index : -1;
        }
        internal static async Task<bool> ConfirmStyledAsync(this TuiApplication app, string message, string confirmLabel = "Yes", string cancelLabel = "No")
        {
            if (confirmLabel == cancelLabel) return await app.SelectStyledAsync(message, confirmLabel).ConfigureAwait(false) == 0;
            return await app.SelectStyledAsync(message, cancelLabel, confirmLabel).ConfigureAwait(false) == 1;
        }
    }
}
