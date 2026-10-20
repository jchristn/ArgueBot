namespace ArgueBot.Core.Security
{
    internal sealed class BuiltInPermissionDefinition
    {
        internal string Key { get; }
        internal string[] ResourceTypes { get; }
        internal string[] Operations { get; }
        internal bool RequiresOwnership { get; }
        internal BuiltInPermissionDefinition(string key, string[] resources, string[] operations, bool ownership)
        { Key = key; ResourceTypes = resources; Operations = operations; RequiresOwnership = ownership; }
    }
}
