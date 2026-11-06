namespace ArgueBot.Core.Models
{
    /// <summary>Restore an earlier revision's title, description, tags and body as a new revision. The name and locked flag are kept.</summary>
    public sealed class ResourceRevertRequest
    {
        /// <summary>Revision to restore. It must still be in the saved history.</summary>
        public long TargetRevision { get; set; }

        /// <summary>Expected current revision of the resource.</summary>
        public long Revision { get; set; }
    }
}
