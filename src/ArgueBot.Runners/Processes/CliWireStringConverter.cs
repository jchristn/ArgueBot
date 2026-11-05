namespace ArgueBot.Runners.Processes
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a CLI event field that ArgueBot uses as text. Agents reuse field names with other shapes
    /// (for example Codex tool items carry an object "result"); those values are skipped instead of
    /// failing the whole stream.
    /// </summary>
    internal sealed class CliWireStringConverter : JsonConverter<string?>
    {
        public override bool HandleNull { get { return true; } }

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number: return reader.TryGetInt64(out long whole) ? whole.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
                case JsonTokenType.True: return "true";
                case JsonTokenType.False: return "false";
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray: reader.Skip(); return null;
                default: return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value == null) writer.WriteNullValue(); else writer.WriteStringValue(value);
        }
    }
}
