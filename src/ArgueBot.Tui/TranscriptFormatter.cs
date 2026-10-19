namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using ArgueBot.Core.Models;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Theming;

    internal static class TranscriptFormatter
    {
        internal static string Heading(Message message)
        {
            List<string> parts = new List<string> { message.Speaker == "user" ? "You" : message.Speaker == "system" ? "System" : message.Speaker };
            if (message.Kind == "steer") parts.Add("Private directive");
            else if (message.Kind != "note" && message.Kind != "answer") parts.Add(Label(message.Kind));
            if (message.CreatedUtc != default) parts.Add(message.CreatedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            if (message.State != "completed") parts.Add(message.State == "streaming" ? "Responding..." : Label(message.State));
            return string.Join("  ·  ", parts);
        }
        private static string Label(string value)
        {
            string text = value.Replace('_', ' ');
            return text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
        internal static void Render(Pane pane, IEnumerable<Message> messages, Theme theme)
        {
            pane.Clear();
            bool first = true;
            foreach (Message message in messages)
            {
                if (!first) { pane.WriteLine(); pane.WriteLine(Text.From("────────────────────────", theme.Border)); pane.WriteLine(); }
                first = false;
                CellStyle headingStyle = message.Speaker == "user" ? theme.Accent : theme.Info;
                pane.WriteLine(Text.From(Heading(message), headingStyle).Bold());
                pane.WriteLine();
                foreach (string line in message.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')) pane.WriteLine(Text.From(line, theme.Text));
                if (message.ContentTruncated)
                {
                    pane.WriteLine();
                    pane.WriteLine(Text.From("Preview only. Press Ctrl+L to read the full message.", theme.Muted));
                }
                pane.WriteLine();
            }
            if (first) pane.WriteLine("Create a conversation with Ctrl+N, then send a note or start a debate with Ctrl+B.");
        }
    }
}
