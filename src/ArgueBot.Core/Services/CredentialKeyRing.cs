namespace ArgueBot.Core.Services
{
    using System.Collections.Generic;

    internal sealed class CredentialKeyRing
    {
        public int Version { get; set; } = 1;
        public string ActiveKeyId { get; set; } = "legacy";
        public Dictionary<string, byte[]> Keys { get; set; } = new Dictionary<string, byte[]>();
    }
}
