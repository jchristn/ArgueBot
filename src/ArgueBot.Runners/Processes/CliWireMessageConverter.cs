namespace ArgueBot.Runners.Processes
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a CLI event's "message" field, which is an object in Claude Code streams but plain text in
    /// Codex diagnostic items (for example configuration warnings). Text becomes a text event.
    /// </summary>
    internal sealed class CliWireMessageConverter : JsonConverter<CliWireEvent?>
    {
        public override CliWireEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String) return new CliWireEvent { Type = "text", Text = reader.GetString() };
            return JsonSerializer.Deserialize<CliWireEvent>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, CliWireEvent? value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
