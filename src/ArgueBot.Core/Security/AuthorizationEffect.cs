namespace ArgueBot.Core.Security
{
    /// <summary>Resolved permission effect. Explicit denial wins over matching permits.</summary>
    public enum AuthorizationEffect
    {
        /// <summary>Allow a matching operation when no matching denial exists.</summary>
        Permit,
        /// <summary>Deny a matching operation.</summary>
        Deny
    }
}
