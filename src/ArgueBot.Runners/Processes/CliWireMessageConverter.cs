namespace ArgueBot.Runners.Processes
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a nested CLI event ("message", "item", "event", "delta"). It is an object in Claude Code streams
    /// but can be plain text, as in Codex configuration warnings. Text becomes a text event; other shapes are skipped.
    /// </summary>
    internal sealed class CliWireMessageConverter : JsonConverter<CliWireEvent?>
    {
        public override CliWireEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { return ReadEvent(ref reader, options); }

        /// <summary>An object becomes an event, text becomes a text event, and any other shape is skipped.</summary>
        internal static CliWireEvent? ReadEvent(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String) return new CliWireEvent { Type = "text", Text = reader.GetString() };
            if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); return null; }
            return JsonSerializer.Deserialize<CliWireEvent>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, CliWireEvent? value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
