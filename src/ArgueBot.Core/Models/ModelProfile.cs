namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>ModelProfile configuration contract.</summary>
    public sealed class ModelProfile
    {
        private string _Id = "";
        /// <summary>Stable reusable model ID.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _EndpointId = "";
        /// <summary>HTTP endpoint the model runs on.</summary>
        public string EndpointId { get { return _EndpointId; } set { System.ArgumentNullException.ThrowIfNull(value); _EndpointId = value; } }

        private string _Model = "";
        /// <summary>Provider model identifier.</summary>
        public string Model { get { return _Model; } set { System.ArgumentNullException.ThrowIfNull(value); _Model = value; } }

        /// <summary>Conservative UTF-16 character budget for this model; not a token count.</summary>
        public int ContextMaxCharacters { get; set; } = 262144;
        /// <summary>Service output capture ceiling, 1024 through 67108864 bytes, for HTTP models.</summary>
        public int MaxOutputBytes { get; set; } = 67108864;
        private ModelGenerationSettings _Generation = new ModelGenerationSettings();
        /// <summary>Optional HTTP sampling and generated-token overrides.</summary>
        public ModelGenerationSettings Generation { get { return _Generation; } set { System.ArgumentNullException.ThrowIfNull(value); _Generation = value; } }

        /// <summary>Whether the model declares tool support.</summary>
        public bool SupportsTools { get; set; } = true;

        /// <summary>Whether new runs may use this model.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; } = 0;

    }
}
