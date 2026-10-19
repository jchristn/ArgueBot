namespace ArgueBot.Core.Security
{
    /// <summary>Validated native principal category; external identity claims never select this value directly.</summary>
    public enum AuthorizationPrincipalKind
    {
        /// <summary>Tenant-bound native user.</summary>
        User,
        /// <summary>Tenant-bound machine credential with an owning user ceiling.</summary>
        Credential,
        /// <summary>Explicitly authenticated platform administrator.</summary>
        Administrator
    }
}
