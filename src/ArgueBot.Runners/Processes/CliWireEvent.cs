namespace ArgueBot.Runners.Processes
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One record from a CLI agent's JSONL stream. Only the fields ArgueBot reads are modeled, and each tolerates
    /// shapes it does not use, so new agent item types (tool calls, web searches, warnings) never break a turn.
    /// </summary>
    internal sealed class CliWireEvent
    {
        [JsonPropertyName("id"), JsonConverter(typeof(CliWireStringConverter))] public string? Id { get; set; }
        [JsonPropertyName("type"), JsonConverter(typeof(CliWireStringConverter))] public string? Type { get; set; }
        [JsonPropertyName("role"), JsonConverter(typeof(CliWireStringConverter))] public string? Role { get; set; }
        [JsonPropertyName("text"), JsonConverter(typeof(CliWireStringConverter))] public string? Text { get; set; }
        [JsonPropertyName("result"), JsonConverter(typeof(CliWireStringConverter))] public string? Result { get; set; }
        [JsonPropertyName("subtype"), JsonConverter(typeof(CliWireStringConverter))] public string? Subtype { get; set; }
        [JsonPropertyName("delta"), JsonConverter(typeof(CliWireMessageConverter))] public CliWireEvent? Delta { get; set; }
        [JsonPropertyName("event"), JsonConverter(typeof(CliWireMessageConverter))] public CliWireEvent? Event { get; set; }
        [JsonPropertyName("item"), JsonConverter(typeof(CliWireMessageConverter))] public CliWireEvent? Item { get; set; }
        [JsonPropertyName("message"), JsonConverter(typeof(CliWireMessageConverter))] public CliWireEvent? Message { get; set; }
        [JsonPropertyName("content"), JsonConverter(typeof(CliWireContentConverter))] public List<CliWireEvent>? Content { get; set; }
    }
}
