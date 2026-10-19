namespace ArgueBot.Server
{
    /// <summary>Protected local enrollment input.</summary>
    public sealed class PairRequest
    {
        /// <summary>Current single-use code read locally by the owner.</summary>
        public string Code { get; set; } = "";
    }
}
