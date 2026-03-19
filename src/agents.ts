import { execa } from "execa";
import stripAnsi from "strip-ansi";
import type { AgentName } from "./types.js";

export interface AgentResult {
  content: string;
  durationMs: number;
  error: string | null;
}

interface CallAgentOptions {
  timeoutMs?: number;
  yoloMode?: boolean;
  onChunk?: (text: string) => void;
}

interface AgentCommand {
  cmd: string;
  args: string[];
}

function buildAgentCommand(agent: AgentName, yoloMode: boolean): AgentCommand {
  switch (agent) {
    case "claude":
      return {
        cmd: "claude",
        args: [
          "-p",
          "-",
          ...(yoloMode ? ["--dangerously-skip-permissions"] : []),
          "--output-format",
          "text",
        ],
      };

    case "codex":
      return {
        cmd: "codex",
        args: [
          "exec",
          ...(yoloMode ? ["--yolo"] : ["--full-auto"]),
          "--skip-git-repo-check",
          "--color",
          "never",
          "-",
        ],
      };

    case "gemini":
      return {
        cmd: "gemini",
        args: [...(yoloMode ? ["--yolo"] : [])],
      };
  }
}

function buildFallbackAgentCommand(agent: AgentName, yoloMode: boolean): AgentCommand | null {
  if (agent === "codex" && yoloMode) {
    return {
      cmd: "codex",
      args: [
        "exec",
        "--dangerously-bypass-approvals-and-sandbox",
        "--skip-git-repo-check",
        "--color",
        "never",
        "-",
      ],
    };
  }

  return null;
}

async function runAgentCommand(
  command: AgentCommand,
  prompt: string,
  timeoutMs: number,
  onChunk?: (text: string) => void,
): Promise<{ content: string; durationMs: number }> {
  const start = Date.now();
  const subprocess = execa(command.cmd, command.args, {
    timeout: timeoutMs,
    env: { ...process.env, NO_COLOR: "1" },
    buffer: true,
    input: prompt,
  });

  let accumulated = "";

  if (subprocess.stdout) {
    subprocess.stdout.on("data", (chunk: Buffer) => {
      const text = stripAnsi(chunk.toString());
      accumulated += text;
      onChunk?.(text);
    });
  }

  await subprocess;

  return {
    content: accumulated.trim(),
    durationMs: Date.now() - start,
  };
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
  options: CallAgentOptions = {},
): Promise<AgentResult> {
  const start = Date.now();
  const timeoutMs = options.timeoutMs ?? 300_000;
  const yoloMode = options.yoloMode ?? false;
  const onChunk = options.onChunk;

  try {
    const result = await runAgentCommand(
      buildAgentCommand(agent, yoloMode),
      prompt,
      timeoutMs,
      onChunk,
    );

    return {
      content: result.content,
      durationMs: result.durationMs,
      error: null,
    };
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : String(err);

    if (yoloMode) {
      const fallbackCommand = buildFallbackAgentCommand(agent, yoloMode);
      const shouldRetry =
        fallbackCommand !== null &&
        /unknown option|unexpected argument|unexpected option|unrecognized option/i.test(message);

      if (shouldRetry) {
        try {
          const result = await runAgentCommand(
            fallbackCommand,
            prompt,
            timeoutMs,
            onChunk,
          );

          return {
            content: result.content,
            durationMs: result.durationMs,
            error: null,
          };
        } catch (fallbackErr: unknown) {
          const fallbackMessage =
            fallbackErr instanceof Error ? fallbackErr.message : String(fallbackErr);
          return {
            content: "",
            durationMs: Date.now() - start,
            error: `Agent "${agent}" failed: ${fallbackMessage}`,
          };
        }
      }
    }

    return {
      content: "",
      durationMs: Date.now() - start,
      error: `Agent "${agent}" failed: ${message}`,
    };
  }
}
