#!/usr/bin/env node

import { runDebate } from "./orchestrator.js";
import type { DebateConfig } from "./types.js";

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
      case "--first":
        if (args[i + 1] === "codex" || args[i + 1] === "claude") {
          config.firstAgent = args[++i] as "claude" | "codex";
        }
        break;
      case "--summary":
        if (args[i + 1] === "codex" || args[i + 1] === "claude") {
          config.summaryAgent = args[++i] as "claude" | "codex";
        }
        break;
      case "--help":
        console.log(`
arguebot - Claude Code vs Codex debate orchestrator

Usage: npx tsx src/index.ts [options]

Options:
  --rounds N            Max debate rounds (default: 5, prompted at startup)
  --timeout N           Intervention window in seconds (default: 10)
  --agent-timeout N     Agent response timeout in seconds (default: 300)
  --first claude|codex  Which agent goes first
  --summary claude|codex  Which agent handles follow-up/summary
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
