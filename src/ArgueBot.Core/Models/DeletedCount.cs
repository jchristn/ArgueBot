namespace ArgueBot.Core.Models
{
    /// <summary>The number of records a bulk delete removed.</summary>
    public sealed class DeletedCount
    {
        /// <summary>Records deleted.</summary>
        public int Deleted { get; set; }
    }
}
