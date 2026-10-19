namespace ArgueBot.Core.Security
{
    using System;
    using System.Collections.Generic;

    internal static class BuiltInRoles
    {
        internal static IReadOnlyList<BuiltInRoleDefinition> All { get; } = new BuiltInRoleDefinition[]
        {
            new BuiltInRoleDefinition("TenantAdmin", new[] { "All" }, new[] { "All" }),
            new BuiltInRoleDefinition("SecurityAdmin", new[] { "User", "Credential", "Session", "Role", "Permission", "Assignment", "Audit", "Tenant" }, new[] { "Read", "Admin" }),
            new BuiltInRoleDefinition("Auditor", new[] { "User", "Credential", "Session", "Role", "Permission", "Assignment", "Audit", "Tenant" }, new[] { "Read" }),
            new BuiltInRoleDefinition("ResourceAdmin", new[] { "All" }, new[] { "All" }),
            new BuiltInRoleDefinition("Editor", new[] { "Conversation", "Message", "Run" }, new[] { "Read", "Write" }),
            new BuiltInRoleDefinition("Viewer", new[] { "Conversation", "Message", "Run" }, new[] { "Read" }),
            new BuiltInRoleDefinition("TenantMember", new[] { "User" }, new[] { "Read" }, true),
            new BuiltInRoleDefinition("Custom", Array.Empty<string>(), Array.Empty<string>())
        };
        internal static BuiltInRoleDefinition? Find(string name)
        {
            foreach (BuiltInRoleDefinition definition in All) if (string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase)) return definition;
            return null;
        }
    }
}
