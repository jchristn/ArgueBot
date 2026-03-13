import * as fs from "node:fs";
import * as path from "node:path";
import type { AgentName, DebateTurn } from "./types.js";

const LABELS: Record<string, string> = {
  claude: "Claude Code",
  codex: "Codex",
  user: "Moderator (User)",
};

export function formatTranscriptMarkdown(
  userPrompt: string,
  transcript: DebateTurn[],
  consensusReached: boolean,
): string {
  const lines: string[] = [];

  lines.push("# ArgueBot Debate Transcript");
  lines.push("");
  lines.push(`**Date:** ${new Date().toISOString()}`);
  lines.push(`**Original Prompt:** ${userPrompt}`);
  lines.push(`**Outcome:** ${consensusReached ? "Consensus Reached" : "No Consensus (Max Rounds)"}`);
  lines.push("");
  lines.push("---");
  lines.push("");

  let lastRound = -1;
  for (const turn of transcript) {
    if (turn.round !== lastRound) {
      lastRound = turn.round;
      if (turn.type !== "final") {
        lines.push(`## Round ${turn.round}`);
        lines.push("");
      }
    }

    if (turn.type === "final") {
      lines.push("## Final Q&A");
      lines.push("");
    }

    const label = LABELS[turn.speaker] ?? turn.speaker;
    lines.push(`### ${label}`);
    lines.push("");
    lines.push(turn.content);
    lines.push("");
    lines.push("---");
    lines.push("");
  }

  return lines.join("\n");
}

export function saveTranscript(
  userPrompt: string,
  transcript: DebateTurn[],
  consensusReached: boolean,
  outputDir: string = ".",
): string {
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
  const filename = `arguebot-transcript-${timestamp}.md`;
  const filepath = path.join(outputDir, filename);
  const content = formatTranscriptMarkdown(userPrompt, transcript, consensusReached);
  fs.writeFileSync(filepath, content, "utf-8");
  return filepath;
}
