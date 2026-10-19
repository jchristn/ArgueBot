namespace ArgueBot.Core.Security
{
    using System.Collections.Generic;

    /// <summary>Current database-resolved user facts and bounded grants, read from one consistent transaction.</summary>
    public sealed class ResolvedAuthorization
    {
        /// <summary>Current user and tenant activity and explicit administrator flags.</summary>
        public AuthorizationPrincipal Principal { get; set; } = new AuthorizationPrincipal();
        /// <summary>Expanded active permissions with assignment and mapping provenance.</summary>
        public List<AuthorizationGrant> Grants { get; set; } = new List<AuthorizationGrant>();
    }
}
