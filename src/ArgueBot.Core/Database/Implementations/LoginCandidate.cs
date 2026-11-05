namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Models;
    using System;

    /// <summary>A sign-in candidate: a user with this email, its tenant and password verifier.</summary>
    internal sealed class LoginCandidate
    {
        internal User User { get; set; } = new User();
        internal Tenant Tenant { get; set; } = new Tenant();
        internal string Hash { get; set; } = "";
        internal string Salt { get; set; } = "";
        internal int Iterations { get; set; }
    }
}
