namespace ArgueBot.Publisher
{
    using System.Collections.Generic;

    /// <summary>Version-independent distribution inputs shared by native packaging jobs.</summary>
    public sealed class PublisherManifest
    {
        /// <summary>Artifact basename.</summary>
        public string Name { get; set; } = "";
        /// <summary>Canonical repository URL.</summary>
        public string Repository { get; set; } = "";
        /// <summary>Application target framework.</summary>
        public string Framework { get; set; } = "";
        /// <summary>Allowed runtime targets.</summary>
        public List<string> RuntimeIdentifiers { get; set; } = new List<string>();
        /// <summary>Executable projects bundled together.</summary>
        public List<string> Projects { get; set; } = new List<string>();
        /// <summary>Packaging channels and their native build operating systems.</summary>
        public Dictionary<string, List<string>> Channels { get; set; } = new Dictionary<string, List<string>>();
    }
}
