namespace ArgueBot.Core.Security
{
    internal sealed class BuiltInRoleDefinition
    {
        internal string Name { get; }
        internal string[] ResourceTypes { get; }
        internal string[] Operations { get; }
        internal bool RequiresOwnership { get; }
        internal BuiltInRoleDefinition(string name, string[] resources, string[] operations, bool ownership = false)
        { Name = name; ResourceTypes = resources; Operations = operations; RequiresOwnership = ownership; }
    }
}
