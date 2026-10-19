namespace ArgueBot.Core.Security
{
    /// <summary>Breadth of a resolved role or credential assignment.</summary>
    public enum AuthorizationScope
    {
        /// <summary>Assignment applies within its tenant; child resources additionally require inheritance.</summary>
        Tenant,
        /// <summary>Assignment applies to one exact authorization resource identifier.</summary>
        Resource
    }
}
