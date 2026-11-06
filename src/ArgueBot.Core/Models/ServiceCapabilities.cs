namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Discoverable debate and configuration limits.</summary>
    public sealed class ServiceCapabilities
    {
        /// <summary>Current multi-agent debate protocol.</summary>
        public int DebateProtocol { get; set; } = 2;
        /// <summary>Default per-run policy; all accepted values are frozen on the run.</summary>
        public DebatePolicy DefaultPolicy { get; set; } = new DebatePolicy();
        /// <summary>Maximum accepted per-run limits. Estimated tokens remain an accounting heuristic.</summary>
        public DebatePolicy MaximumPolicy { get; set; } = new DebatePolicy { MaxParticipantTurns = 100000, MaxOrchestrationCalls = 10000, MaxCompactionCalls = 10000, MaxToolCalls = 100000, MaxWallClockSeconds = 604800, MaxContextCharacters = 16777216, MaxEstimatedInputTokens = int.MaxValue, MaxResourceWritesPerTurn = 100, MaxResourceWriteBytesPerTurn = 1048576, MaxResourcesCreated = 1000 };
        /// <summary>Maximum active seats accepted for a new roster.</summary>
        public int MaxParticipants { get; set; } = 16;
        private List<string> _DeliveryModes = new List<string> { "interrupt_now", "next_turn" };
        /// <summary>Supported per-interjection delivery modes.</summary>
        public List<string> DeliveryModes { get { return _DeliveryModes; } set { ArgumentNullException.ThrowIfNull(value); _DeliveryModes = value; } }
        private string _ConsensusPolicy = "unanimous";
        /// <summary>Every active participant must explicitly agree.</summary>
        public string ConsensusPolicy { get { return _ConsensusPolicy; } set { ArgumentNullException.ThrowIfNull(value); _ConsensusPolicy = value; } }
        /// <summary>Roster changes are applied at safe boundaries.</summary>
        public bool RosterChanges { get; set; } = true;
    }
}
