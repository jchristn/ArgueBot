namespace ArgueBot.Core.Security
{
    using System.Collections.Generic;

    internal sealed class BuiltInRoleDefinition
    {
        internal string Name { get; }
        internal string[] ResourceTypes { get; }
        internal string[] Operations { get; }
        internal bool RequiresOwnership { get; }
        internal List<BuiltInPermissionDefinition> AdditionalPermissions { get; } = new List<BuiltInPermissionDefinition>();
        internal IReadOnlyList<BuiltInPermissionDefinition> Permissions
        {
            get
            {
                List<BuiltInPermissionDefinition> result = new List<BuiltInPermissionDefinition>();
                if (ResourceTypes.Length > 0) result.Add(new BuiltInPermissionDefinition("default", ResourceTypes, Operations, RequiresOwnership));
                result.AddRange(AdditionalPermissions); return result;
            }
        }
        internal BuiltInRoleDefinition(string name, string[] resources, string[] operations, bool ownership = false)
        { Name = name; ResourceTypes = resources; Operations = operations; RequiresOwnership = ownership; }
    }
}
