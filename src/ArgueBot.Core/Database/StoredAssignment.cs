namespace ArgueBot.Core.Database
{
    using ArgueBot.Core.Security;

    internal sealed class StoredAssignment
    {
        internal string Id { get; set; } = "";
        internal string RoleId { get; set; } = "";
        internal string RoleName { get; set; } = "";
        internal AuthorizationScope Scope { get; set; }
        internal string ResourceId { get; set; } = "";
        internal bool Inherits { get; set; }
    }
}
