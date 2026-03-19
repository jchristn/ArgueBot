import { readFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
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

interface GeminiAuthDiagnostics {
  selectedType: string | null;
  settingsPath: string;
}

function readJsonFile<T>(filePath: string): T | null {
  try {
    return JSON.parse(readFileSync(filePath, "utf8")) as T;
  } catch {
    return null;
  }
}

function getGeminiAuthDiagnostics(): GeminiAuthDiagnostics {
  const settingsPath = path.join(os.homedir(), ".gemini", "settings.json");
  const settings = readJsonFile<{
    security?: { auth?: { selectedType?: string } };
  }>(settingsPath);

  return {
    selectedType: settings?.security?.auth?.selectedType ?? null,
    settingsPath,
  };
}

function getClaudeSetupError(): string | null {
  if (process.env.ANTHROPIC_API_KEY) {
    return null;
  }

  const credentialsPath = path.join(os.homedir(), ".claude", ".credentials.json");
  const credentials = readJsonFile<{
    claudeAiOauth?: {
      accessToken?: string;
      refreshToken?: string;
    };
  }>(credentialsPath);

  const hasOauthCredentials = Boolean(
    credentials?.claudeAiOauth?.accessToken && credentials.claudeAiOauth.refreshToken,
  );

  if (hasOauthCredentials) {
    return null;
  }

  return `Agent "claude" failed: Claude Code is not authenticated for headless use. Set ANTHROPIC_API_KEY or run \`claude auth\`. ArgueBot invokes Claude in non-interactive \`-p\` mode.`;
}

function getCodexSetupError(): string | null {
  if (process.env.OPENAI_API_KEY) {
    return null;
  }

  const authPath = path.join(os.homedir(), ".codex", "auth.json");
  const auth = readJsonFile<{
    OPENAI_API_KEY?: string;
    tokens?: {
      access_token?: string;
      refresh_token?: string;
    };
  }>(authPath);

  const hasStoredApiKey = Boolean(auth?.OPENAI_API_KEY);
  const hasOauthTokens = Boolean(auth?.tokens?.access_token && auth.tokens.refresh_token);

  if (hasStoredApiKey || hasOauthTokens) {
    return null;
  }

  return 'Agent "codex" failed: Codex is not authenticated for headless use. Set OPENAI_API_KEY or run `codex login`. ArgueBot invokes Codex with `codex exec`.';
}

function buildGeminiAuthErrorMessage(): string {
  const { selectedType, settingsPath } = getGeminiAuthDiagnostics();
  const hasGeminiApiKey = Boolean(process.env.GEMINI_API_KEY);
  const hasGoogleApiKey = Boolean(process.env.GOOGLE_API_KEY);
  const hasVertexProjectConfig =
    Boolean(process.env.GOOGLE_CLOUD_PROJECT) && Boolean(process.env.GOOGLE_CLOUD_LOCATION);

  if (selectedType === "gemini-api-key" && !hasGeminiApiKey) {
    return `Agent "gemini" failed: Gemini CLI is configured for API key auth in ${settingsPath}, but GEMINI_API_KEY is not set in this shell or project .env. Run \`gemini /auth\` and switch to Google login, or export GEMINI_API_KEY before starting ArgueBot.`;
  }

  if (selectedType === "vertex-ai" && !hasGoogleApiKey && !hasVertexProjectConfig) {
    return `Agent "gemini" failed: Gemini CLI is configured for Vertex AI auth in ${settingsPath}, but this shell is missing either GOOGLE_API_KEY or both GOOGLE_CLOUD_PROJECT and GOOGLE_CLOUD_LOCATION.`;
  }

  return 'Agent "gemini" failed: Gemini CLI is not authenticated for headless use. Run `gemini /auth` and choose Google login, or set the environment variables required by your selected Gemini auth mode before starting ArgueBot.';
}

function getGeminiSetupError(): string | null {
  const message = buildGeminiAuthErrorMessage();

  if (message.includes("but GEMINI_API_KEY is not set") || message.includes("but this shell is missing")) {
    return message;
  }

  return null;
}

export function validateAgentSetup(agent: AgentName): string | null {
  switch (agent) {
    case "claude":
      return getClaudeSetupError();

    case "codex":
      return getCodexSetupError();

    case "gemini":
      return getGeminiSetupError();
  }
}

function resolveGeminiCommand(): string {
  return process.platform === "win32" ? "gemini.cmd" : "gemini";
}

function resolveCodexCommand(): string {
  return process.platform === "win32" ? "codex.cmd" : "codex";
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
        cmd: resolveCodexCommand(),
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
        cmd: resolveGeminiCommand(),
        args: [
          "--prompt",
          "",
          "--output-format",
          "text",
          ...(yoloMode ? ["--yolo"] : []),
        ],
      };
  }
}

function buildFallbackAgentCommand(agent: AgentName, yoloMode: boolean): AgentCommand | null {
  if (agent === "codex" && yoloMode) {
    return {
      cmd: resolveCodexCommand(),
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
  const setupError = validateAgentSetup(agent);

  if (setupError) {
    return {
      content: "",
      durationMs: Date.now() - start,
      error: setupError,
    };
  }

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

    if (
      agent === "claude" &&
      /auth|login|api[_ -]?key|invalid[_ -]?x[_ -]?api[_ -]?key|not authenticated/i.test(message)
    ) {
      return {
        content: "",
        durationMs: Date.now() - start,
        error: getClaudeSetupError() ?? `Agent "claude" failed: ${message}`,
      };
    }

    if (
      agent === "codex" &&
      /auth|login|api[_ -]?key|not logged in|not authenticated|credentials/i.test(message)
    ) {
      return {
        content: "",
        durationMs: Date.now() - start,
        error: getCodexSetupError() ?? `Agent "codex" failed: ${message}`,
      };
    }

    if (agent === "gemini" && /api[_ -]?key/i.test(message)) {
      return {
        content: "",
        durationMs: Date.now() - start,
        error: buildGeminiAuthErrorMessage(),
      };
    }

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
