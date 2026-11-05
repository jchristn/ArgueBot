namespace ArgueBot.Runners.Processes
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Reads a CLI event's "content", which is usually a list of parts but may be plain text or another shape.</summary>
    internal sealed class CliWireContentConverter : JsonConverter<List<CliWireEvent>?>
    {
        public override List<CliWireEvent>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String) return new List<CliWireEvent> { new CliWireEvent { Type = "text", Text = reader.GetString() } };
            if (reader.TokenType != JsonTokenType.StartArray) { reader.Skip(); return null; }
            List<CliWireEvent> parts = new List<CliWireEvent>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                CliWireEvent? part = CliWireMessageConverter.ReadEvent(ref reader, options);
                if (part != null) parts.Add(part);
            }
            return parts;
        }

        public override void Write(Utf8JsonWriter writer, List<CliWireEvent>? value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
