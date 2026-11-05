namespace ArgueBot.Tui
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Theming;

    internal static class TranscriptFormatter
    {
        internal static string Heading(Message message)
        {
            List<string> parts = new List<string> { message.Speaker == "user" ? "You" : message.Speaker == "system" ? "System" : message.Speaker };
            if (message.Kind == "tool") parts.Add(message.Content.StartsWith("Conversation tool:", StringComparison.Ordinal) ? "Conversation tool" : "Local tool");
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
        internal static void Render(Pane pane, IEnumerable<Message> messages, Theme theme, string? conversationTitle = null, IReadOnlyDictionary<string, string>? failures = null)
        {
            pane.TailFollow.Mode = TailFollowMode.FollowAtBottom;
            pane.Clear();
            bool first = true;
            foreach (Message message in MessagePresentation.MergeToolActivity(messages))
            {
                if (!first) { pane.WriteLine(); pane.WriteLine(Text.From("────────────────────────", theme.Border)); pane.WriteLine(); }
                first = false;
                CellStyle headingStyle = message.Speaker == "user" ? theme.Accent : theme.Info;
                pane.WriteLine(Text.From(Heading(message), headingStyle).Bold());
                pane.WriteMarkdown(MessagePresentation.Markdown(message), new MarkdownRenderOptions().WithTheme(theme));
                if (message.State == "failed" || message.State == "interrupted")
                {
                    pane.WriteLine(Text.From((message.State == "interrupted" ? "TURN INTERRUPTED: " : "TURN FAILED: ") + message.Speaker, theme.Error).Bold());
                    string explanation = failures != null && failures.TryGetValue(message.Id, out string? detail) && detail.Length > 0 ? detail : "The agent could not finish this turn. Press Ctrl+G for generation attempts and the failure cause; Ctrl+E opens Agents.";
                    pane.WriteLine(Text.From(explanation, theme.Error));
                }
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
                pane.WriteLine("Agents: add at least two agents (CLI programs or HTTP models).");
                pane.WriteLine(conversationTitle == null ? "New, then Start debate: choose a panel and an orchestrator." : "Start debate: choose a panel, orchestrator and limits.");
                pane.WriteLine("Review the setup before starting your agents.");
                pane.WriteLine();
                pane.WriteLine("Menu lists all actions. Help explains each one.");
                pane.WriteLine("Tab moves focus; Enter chooses an action.");
                pane.WriteLine();
                pane.WriteLine(Text.From("Add context to guide a running debate. Use Follow-up to request a new answer.", theme.Muted));
                pane.TailFollow.Mode = TailFollowMode.Never;
                pane.ScrollUp(pane.ScrollbackCapacityLines);
            }
        }
    }
}
