namespace ArgueBot.Runners.Processes
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    internal sealed class CliWireEvent
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("result")] public string? Result { get; set; }
        [JsonPropertyName("subtype")] public string? Subtype { get; set; }
        [JsonPropertyName("delta")] public CliWireEvent? Delta { get; set; }
        [JsonPropertyName("event")] public CliWireEvent? Event { get; set; }
        [JsonPropertyName("item")] public CliWireEvent? Item { get; set; }
        [JsonPropertyName("message")] public CliWireEvent? Message { get; set; }
        [JsonPropertyName("content")] public List<CliWireEvent>? Content { get; set; }
    }
}
