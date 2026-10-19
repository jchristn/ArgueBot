namespace ArgueBot.Core.Database
{
    internal sealed class ResolvedRoleReference
    {
        internal string Id { get; set; } = "";
        internal string Name { get; set; } = "";
        internal bool Active { get; set; }
        internal bool BaselineOnly { get; set; }
    }
}
