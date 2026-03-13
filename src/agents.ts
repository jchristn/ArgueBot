import { execa } from "execa";
import stripAnsi from "strip-ansi";
import type { AgentName } from "./types.js";

export interface AgentResult {
  content: string;
  durationMs: number;
  error: string | null;
}

/**
 * Call an agent with streaming output. The onChunk callback receives each
 * chunk of text as it arrives so the UI can display it in real-time.
 * Returns the full accumulated response when the process exits.
 *
 * Prompts are piped via stdin (not CLI args) to avoid shell escaping
 * issues and OS argument length limits.
 */
export async function callAgent(
  agent: AgentName,
  prompt: string,
  timeoutMs: number = 300_000,
  onChunk?: (text: string) => void,
): Promise<AgentResult> {
  const start = Date.now();

  try {
    let args: string[];
    let cmd: string;

    if (agent === "claude") {
      // claude -p reads prompt from stdin when "-" is passed as the prompt arg
      cmd = "claude";
      args = ["-p", "-", "--dangerously-skip-permissions", "--output-format", "text"];
    } else {
      // codex exec reads prompt from stdin when "-" is passed as the prompt arg
      cmd = "codex";
      args = ["exec", "--full-auto", "--skip-git-repo-check", "--color", "never", "-"];
    }

    const subprocess = execa(cmd, args, {
      timeout: timeoutMs,
      env: { ...process.env, NO_COLOR: "1" },
      buffer: true,
      // Pipe the prompt into stdin
      input: prompt,
    });

    let accumulated = "";

    if (subprocess.stdout) {
      subprocess.stdout.on("data", (chunk: Buffer) => {
        const text = stripAnsi(chunk.toString());
        accumulated += text;
        if (onChunk) {
          onChunk(text);
        }
      });
    }

    await subprocess;

    const content = accumulated.trim();

    return {
      content,
      durationMs: Date.now() - start,
      error: null,
    };
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : String(err);
    return {
      content: "",
      durationMs: Date.now() - start,
      error: `Agent "${agent}" failed: ${message}`,
    };
  }
}
