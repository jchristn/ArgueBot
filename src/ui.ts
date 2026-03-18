import chalk from "chalk";
import * as readline from "node:readline";
import type { AgentName, DebateState, DebateTurn } from "./types.js";
import { readTextInput } from "./input.js";

const COLORS = {
  claude: chalk.blue,
  codex: chalk.yellow,
  user: chalk.green,
  system: chalk.gray,
  error: chalk.red,
  highlight: chalk.cyan.bold,
};

const LABELS: Record<string, string> = {
  claude: "Claude Code",
  codex: "Codex",
  user: "You",
  system: "System",
};

export function printBanner(): void {
  console.log();
  console.log(COLORS.highlight("ArgueBot v0.1 -- Claude Code vs Codex Debate"));
  console.log(COLORS.system("─".repeat(47)));
  console.log();
}

export function printAgentHeader(agent: AgentName): void {
  const colorFn = COLORS[agent];
  const label = LABELS[agent];
  console.log();
  console.log(colorFn(`${label} >`));
}

export function printTurn(turn: DebateTurn): void {
  const colorFn = COLORS[turn.speaker] ?? COLORS.system;
  const label = LABELS[turn.speaker] ?? turn.speaker;

  console.log();
  console.log(colorFn(`${label} >`));
  console.log();
  const lines = turn.content.split("\n");
  for (const line of lines) {
    console.log(line);
  }
  console.log();
}

export function printRoundHeader(round: number, maxRounds?: number): void {
  if (maxRounds && isFinite(maxRounds)) {
    console.log(COLORS.system(`[Round ${round}/${maxRounds}]`));
  } else {
    console.log(COLORS.system(`[Round ${round}]`));
  }
}

export function printSystemMessage(message: string): void {
  console.log(COLORS.system(message));
}

export function printError(message: string): void {
  console.log(COLORS.error(`Error: ${message}`));
}

export function printConsensus(reached: boolean): void {
  console.log();
  if (reached) {
    console.log(COLORS.highlight(">>> Consensus reached! <<<"));
  } else {
    console.log(COLORS.system(">>> Max rounds reached. No full consensus. <<<"));
  }
  console.log();
}

export function printStatus(state: DebateState, speaker?: AgentName): void {
  if (state === "agent_turn" && speaker) {
    const colorFn = COLORS[speaker];
    const label = LABELS[speaker];
    printSystemMessage(`${colorFn(label)} is thinking...`);
  }
}

export async function askUser(
  prompt: string,
  options: { multiline?: boolean } = {},
): Promise<string> {
  if (!options.multiline) {
    return new Promise((resolve) => {
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
      });
      rl.question(COLORS.user(prompt), (answer) => {
        rl.close();
        resolve(answer);
      });
    });
  }

  const result = await readTextInput({
    prompt,
    multiline: options.multiline ?? false,
  });
  return result ?? "";
}

/**
 * Timed intervention window. Shows a countdown. If the user starts typing,
 * the countdown pauses and waits for them to press Enter. If the countdown
 * expires, returns null (meaning "continue without intervention").
 */
export function waitForIntervention(timeoutMs: number): Promise<string | null> {
  return readTextInput({
    prompt: "You > ",
    multiline: true,
    timeoutMs,
  });
}

export function printHelp(): void {
  console.log();
  console.log(COLORS.system("Intervention commands:"));
  console.log(COLORS.system('  /steer "directive"  - Steer the debate direction'));
  console.log(COLORS.system("  /pause              - Pause the debate"));
  console.log(COLORS.system("  /stop               - Stop debate, go to final Q&A"));
  console.log(COLORS.system("  /extend N           - Add N more rounds"));
  console.log(COLORS.system("  /reset              - Start over"));
  console.log(COLORS.system("  /help               - Show this help"));
  console.log(COLORS.system("  (any text)          - Inject a moderator note"));
  console.log();
}

export function printTranscriptSummary(transcript: DebateTurn[]): void {
  console.log();
  console.log(COLORS.highlight("=== Full Debate Transcript ==="));
  console.log();
  for (const turn of transcript) {
    const colorFn = COLORS[turn.speaker] ?? COLORS.system;
    const label = LABELS[turn.speaker] ?? turn.speaker;
    console.log(colorFn(`[${label}] (Round ${turn.round}, ${turn.type})`));
    const lines = turn.content.split("\n").slice(0, 2);
    for (const line of lines) {
      console.log(chalk.dim(`  ${line}`));
    }
    if (turn.content.split("\n").length > 2) {
      console.log(chalk.dim("  ..."));
    }
    console.log();
  }
}
