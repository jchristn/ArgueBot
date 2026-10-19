namespace ArgueBot.Core.Security
{
    using System.Collections.Generic;

    /// <summary>Deterministic authorization result with bounded provenance and explicit bypass/owner-ceiling accounting facts.</summary>
    public sealed class AuthorizationDecision
    {
        /// <summary>Whether every required operation is permitted.</summary>
        public bool Allowed { get; set; }
        /// <summary>Stable decision code, independent of localized user-facing text.</summary>
        public string Reason { get; set; } = "implicit_deny";
        /// <summary>Matching grant identifiers, bounded by the evaluator's input limits.</summary>
        public List<string> MatchedGrantIds { get; set; } = new List<string>();
        /// <summary>A direct user/administrator bypass authorized the operation. Credentials never receive this bypass.</summary>
        public bool UsedAdminBypass { get; set; }
        /// <summary>The credential decision was intersected with the owning user's authority.</summary>
        public bool OwnerCeilingApplied { get; set; }
        /// <summary>The owner ceiling was satisfied by an administrator flag rather than an inherited credential privilege.</summary>
        public bool OwnerCeilingUsedAdminBypass { get; set; }
    }
}
