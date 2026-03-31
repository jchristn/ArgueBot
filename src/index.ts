#!/usr/bin/env node

import "dotenv/config";
import { runDebate } from "./orchestrator.js";
import type { AgentName, DebateConfig } from "./types.js";

function isAgentName(value: string | undefined): value is AgentName {
  return value === "claude" || value === "codex" || value === "gemini";
}

function parseArgs(): Partial<DebateConfig> {
  const args = process.argv.slice(2);
  const config: Partial<DebateConfig> = {};

  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case "--rounds":
        config.maxRounds = parseInt(args[++i], 10);
        break;
      case "--timeout":
        config.interventionTimeoutMs = parseInt(args[++i], 10) * 1000;
        break;
      case "--agent-timeout":
        config.agentTimeoutMs = parseInt(args[++i], 10) * 1000;
        break;
      case "--yolo":
        config.yoloMode = true;
        break;
      case "-v":
      case "--verbose":
        config.verbose = true;
        break;
      case "--first":
        if (isAgentName(args[i + 1])) {
          config.firstAgent = args[++i] as AgentName;
        }
        break;
      case "--second":
        if (isAgentName(args[i + 1])) {
          config.secondAgent = args[++i] as AgentName;
        }
        break;
      case "--summary":
        if (isAgentName(args[i + 1])) {
          config.summaryAgent = args[++i] as AgentName;
        }
        break;
      case "--help":
        console.log(`
arguebot - multi-agent debate orchestrator

Usage: npx tsx src/index.ts [options]

Options:
  --rounds N            Max debate rounds (default: 5, prompted at startup)
  --timeout N           Intervention window in seconds (default: 10)
  --agent-timeout N     Agent response timeout in seconds (default: 300)
  --yolo                Enable dangerous/no-confirmation mode for agent CLIs
  -v, --verbose         Show low-level agent status/event messages
  --first claude|codex|gemini   Which agent goes first
  --second claude|codex|gemini  Which agent is the other debater
  --summary claude|codex|gemini Which agent handles follow-up/summary
  --help                Show this help
`);
        process.exit(0);
    }
  }

  return config;
}

const config = parseArgs();
runDebate(config).catch((err) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
