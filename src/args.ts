import type { AgentName, DebateConfig } from "./types.js";

export function isAgentName(value: unknown): value is AgentName {
  return value === "claude" || value === "codex" || value === "gemini";
}

/**
 * Parse one debate flag shared by the terminal and web UIs into `config`.
 * Returns the index of the last argument consumed, or -1 if `args[i]` is
 * not a debate flag.
 */
export function parseDebateFlag(args: string[], i: number, config: Partial<DebateConfig>): number {
  switch (args[i]) {
    case "--rounds":
      config.maxRounds = parseInt(args[i + 1], 10);
      return i + 1;
    case "--timeout":
      config.interventionTimeoutMs = parseInt(args[i + 1], 10) * 1000;
      return i + 1;
    case "--agent-timeout":
      config.agentTimeoutMs = parseInt(args[i + 1], 10) * 1000;
      return i + 1;
    case "--yolo":
      config.yoloMode = true;
      return i;
    case "-v":
    case "--verbose":
      config.verbose = true;
      return i;
    case "--first":
    case "--second":
    case "--summary": {
      if (!isAgentName(args[i + 1])) return i;
      const key = args[i] === "--first" ? "firstAgent" : args[i] === "--second" ? "secondAgent" : "summaryAgent";
      config[key] = args[i + 1] as AgentName;
      return i + 1;
    }
    default:
      return -1;
  }
}
