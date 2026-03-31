export type AgentName = "claude" | "codex" | "gemini";

export interface DebateTurn {
  round: number;
  speaker: AgentName | "user";
  content: string;
  timestamp: Date;
  type: "opening" | "rebuttal" | "moderator" | "consensus" | "final";
}

export type DebateState =
  | "idle"
  | "user_input"
  | "agent_turn"
  | "waiting_intervention"
  | "paused"
  | "consensus_reached"
  | "max_rounds_reached"
  | "user_final_query"
  | "done";

export interface DebateConfig {
  maxRounds: number;
  interventionTimeoutMs: number;
  agentTimeoutMs: number;
  yoloMode: boolean;
  verbose: boolean;
  firstAgent: AgentName;
  secondAgent: AgentName;
  summaryAgent: AgentName;
}

export interface DebateSession {
  id: string;
  userPrompt: string;
  config: DebateConfig;
  state: DebateState;
  transcript: DebateTurn[];
  currentRound: number;
  currentSpeaker: AgentName;
  steerDirective: string | null;
  consensusReached: boolean;
}

export const DEFAULT_CONFIG: DebateConfig = {
  maxRounds: 5,
  interventionTimeoutMs: 10_000,
  agentTimeoutMs: 300_000,
  yoloMode: false,
  verbose: false,
  firstAgent: "claude",
  secondAgent: "codex",
  summaryAgent: "claude",
};
