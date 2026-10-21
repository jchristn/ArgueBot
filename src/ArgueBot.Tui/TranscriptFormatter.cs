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
            if (message.Kind == "tool") parts.Add("Local tool");
            else if (message.Kind == "steer") parts.Add("Private directive");
            else if (message.Kind != "note" && message.Kind != "answer") parts.Add(Label(message.Kind));
            if (message.CreatedUtc != default) parts.Add(message.CreatedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            if (message.State != "completed") parts.Add(message.State == "streaming" ? "Responding..." : Label(message.State));
            return string.Join(" - ", parts);
        }
        private static string Label(string value)
        {
            string text = value.Replace('_', ' ');
            return text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
        internal static void Render(Pane pane, IEnumerable<Message> messages, Theme theme, string? conversationTitle = null)
        {
            pane.TailFollow.Mode = TailFollowMode.FollowAtBottom;
            pane.Clear();
            bool first = true;
            foreach (Message message in messages)
            {
                if (!first) { pane.WriteLine(); pane.WriteLine(Text.From("────────────────────────", theme.Border)); pane.WriteLine(); }
                first = false;
                CellStyle headingStyle = message.Speaker == "user" ? theme.Accent : theme.Info;
                pane.WriteLine(Text.From(Heading(message), headingStyle).Bold());
                pane.WriteMarkdown(message.Content, new MarkdownRenderOptions().WithTheme(theme));
                if (message.State == "failed" && message.Content.Length == 0) pane.WriteLine(Text.From("The agent produced no answer. Press g in the conversation list to inspect the failure; e opens Agents to check its settings.", theme.Error));
                if (message.ContentTruncated)
                {
                    pane.WriteLine();
                    pane.WriteLine(Text.From("Preview only. Press Ctrl+L to read the full message.", theme.Muted));
                }
                pane.WriteLine();
            }
            if (first)
            {
                pane.WriteLine(Text.From(conversationTitle == null ? "Welcome to ArgueBot" : "Your conversation is ready", theme.Accent).Bold());
                pane.WriteLine();
                if (conversationTitle != null) { pane.WriteLine(Text.From(conversationTitle, theme.Text)); pane.WriteLine(); }
                pane.WriteLine("Agents: add two CLI harnesses or REST models.");
                pane.WriteLine(conversationTitle == null ? "New: follow the guided debate setup." : "Start debate: choose speakers and settings.");
                pane.WriteLine("Review the setup before starting your agents.");
                pane.WriteLine();
                pane.WriteLine("Menu lists all actions. Help explains each one.");
                pane.WriteLine("Tab moves focus; Enter chooses an action.");
                pane.WriteLine();
                pane.WriteLine(Text.From("Notes add context. They do not start an agent reply.", theme.Muted));
                pane.TailFollow.Mode = TailFollowMode.Never;
                pane.ScrollUp(pane.ScrollbackCapacityLines);
            }
        }
    }
}
