namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>EndpointProfile configuration contract.</summary>
    public sealed class EndpointProfile
    {
        private string _Id = "";
        /// <summary>Stable endpoint ID.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Protocol = "ollama";
        /// <summary>ollama or openai HTTP protocol.</summary>
        public string Protocol { get { return _Protocol; } set { System.ArgumentNullException.ThrowIfNull(value); _Protocol = value; } }

        private string _BaseUrl = "http://127.0.0.1:11434";
        /// <summary>HTTP base URL without embedded credentials, query or fragment.</summary>
        public string BaseUrl { get { return _BaseUrl; } set { System.ArgumentNullException.ThrowIfNull(value); _BaseUrl = value; } }

        /// <summary>Generation deadline, 1 through 3600 seconds.</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Shared endpoint generation capacity, 1 through 64; changes affect subsequent admissions.</summary>
        public int MaxConcurrentInvocations { get; set; } = 1;

        /// <summary>Whether new invocations may use this connection.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; } = 0;

        private List<HttpAuthBinding> _AuthBindings = new List<HttpAuthBinding>();
        /// <summary>Ordered HTTP authentication and routing bindings.</summary>
        public List<HttpAuthBinding> AuthBindings { get { return _AuthBindings; } set { System.ArgumentNullException.ThrowIfNull(value); _AuthBindings = value; } }

    }
}
