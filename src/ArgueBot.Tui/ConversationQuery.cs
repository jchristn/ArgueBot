namespace ArgueBot.Tui
{
    internal sealed class ConversationQuery
    {
        internal long Revision { get; init; }
        internal string Search { get; init; } = "";
        internal bool Archived { get; init; }
        internal int PageNumber { get; init; }
        internal string? SelectedId { get; init; }
        internal string? SelectedTitle { get; init; }
    }
}
