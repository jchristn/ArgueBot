namespace ArgueBot.Core.Models
{
    /// <summary>Durable reservations; failures and explicit retries consume their own invocation allowances.</summary>
    public sealed class DebateUsage
    {
        /// <summary>Reserved participant turns for this run.</summary>
        public long ParticipantTurns { get; set; }
        /// <summary>Reserved orchestration calls for this run.</summary>
        public long OrchestrationCalls { get; set; }
        /// <summary>Reserved compaction calls for this run.</summary>
        public long CompactionCalls { get; set; }
        /// <summary>Reserved tool calls for this run.</summary>
        public long ToolCalls { get; set; }
        /// <summary>Reserved estimated input tokens for this run.</summary>
        public long EstimatedInputTokens { get; set; }
        /// <summary>Resources this run's agents created.</summary>
        public long ResourcesCreated { get; set; }
        /// <summary>Initial prompt UTF-8 bytes plus envelope allowance; not billed provider tokens or continuation accounting.</summary>
        public string TokenEstimateMethod { get; set; } = "initial-input-utf8-bytes-plus-envelope";
    }
}
