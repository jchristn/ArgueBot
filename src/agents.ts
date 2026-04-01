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

export interface AgentStreamEvent {
  type: "delta" | "status";
  text: string;
}

interface CallAgentOptions {
  timeoutMs?: number;
  yoloMode?: boolean;
  onEvent?: (event: AgentStreamEvent) => void;
}

interface AgentCommand {
  cmd: string;
  args: string[];
  streamFormat: "text" | "jsonl";
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
          "--verbose",
          "--output-format",
          "stream-json",
          "--include-partial-messages",
        ],
        streamFormat: "jsonl",
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
          "--json",
          "-",
        ],
        streamFormat: "jsonl",
      };

    case "gemini":
      return {
        cmd: resolveGeminiCommand(),
        args: [
          "--prompt",
          "",
          "--output-format",
          "stream-json",
          ...(yoloMode ? ["--yolo"] : []),
        ],
        streamFormat: "jsonl",
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
        "--json",
        "-",
      ],
      streamFormat: "jsonl",
    };
  }

  return null;
}

function normalizeWhitespace(text: string): string {
  return text.replace(/\r/g, "");
}

function appendUniqueText(target: string, text: string): string {
  if (!text) {
    return target;
  }

  if (!target) {
    return text;
  }

  if (target.endsWith(text)) {
    return target;
  }

  return target + text;
}

function extractNovelText(existing: string, incoming: string): string {
  if (!incoming) {
    return "";
  }

  if (!existing) {
    return incoming;
  }

  if (existing.endsWith(incoming) || existing.includes(incoming)) {
    return "";
  }

  const maxOverlap = Math.min(existing.length, incoming.length);
  for (let overlapLength = maxOverlap; overlapLength > 0; overlapLength -= 1) {
    if (existing.slice(-overlapLength) === incoming.slice(0, overlapLength)) {
      return incoming.slice(overlapLength);
    }
  }

  return incoming;
}

function sanitizeLine(text: string): string {
  return stripAnsi(normalizeWhitespace(text)).trim();
}

function readNestedString(value: unknown, path: string[]): string | null {
  let current: unknown = value;
  for (const segment of path) {
    if (!current || typeof current !== "object" || !(segment in current)) {
      return null;
    }
    current = (current as Record<string, unknown>)[segment];
  }

  return typeof current === "string" && current.length > 0 ? current : null;
}

function readTextFromContentBlocks(value: unknown): string | null {
  if (!Array.isArray(value)) {
    return null;
  }

  const text = value
    .map((item) => {
      if (!item || typeof item !== "object") {
        return "";
      }

      const record = item as Record<string, unknown>;
      if (typeof record.text === "string") {
        return record.text;
      }
      if (record.type === "output_text" && typeof record.text === "string") {
        return record.text;
      }

      return "";
    })
    .join("");

  return text.length > 0 ? text : null;
}

function normalizeClaudeEvent(event: Record<string, unknown>): AgentStreamEvent[] {
  const type = typeof event.type === "string" ? event.type : "";
  const subtype = typeof event.subtype === "string" ? event.subtype : "";
  const innerEvent =
    event.event && typeof event.event === "object" ? (event.event as Record<string, unknown>) : null;
  const innerType = innerEvent && typeof innerEvent.type === "string" ? innerEvent.type : "";
  const events: AgentStreamEvent[] = [];

  const deltaText =
    readNestedString(event, ["result"]) ??
    readNestedString(innerEvent, ["delta", "text"]) ??
    readNestedString(innerEvent, ["content_block", "text"]) ??
    readNestedString(event, ["delta", "text"]) ??
    readNestedString(event, ["content_block", "text"]) ??
    readNestedString(event, ["message", "content", "0", "text"]) ??
    readNestedString(innerEvent, ["message", "content", "0", "text"]) ??
    readTextFromContentBlocks(event.message && typeof event.message === "object"
      ? (event.message as Record<string, unknown>).content
      : undefined) ??
    readTextFromContentBlocks(innerEvent && innerEvent.message && typeof innerEvent.message === "object"
      ? (innerEvent.message as Record<string, unknown>).content
      : undefined);
  if (deltaText) {
    events.push({ type: "delta", text: deltaText });
  }

  const statusText =
    readNestedString(event, ["error", "message"]) ??
    readNestedString(event, ["result", "error"]) ??
    readNestedString(innerEvent, ["error", "message"]) ??
    readNestedString(event, ["message"]) ??
    readNestedString(event, ["status"]);

  if (statusText && !deltaText) {
    events.push({ type: "status", text: `${type}${subtype ? `:${subtype}` : ""} ${statusText}`.trim() });
  } else if (!deltaText) {
    const statusLabel =
      [type, subtype || innerType].filter(Boolean).join(":") ||
      innerType;
    if (statusLabel) {
      events.push({ type: "status", text: statusLabel });
    }
  }

  return events;
}

function normalizeCodexEvent(event: Record<string, unknown>): AgentStreamEvent[] {
  const type = typeof event.type === "string" ? event.type : "";
  const events: AgentStreamEvent[] = [];

  const deltaText =
    readNestedString(event, ["result"]) ??
    readNestedString(event, ["delta"]) ??
    readNestedString(event, ["text"]) ??
    readNestedString(event, ["content"]) ??
    readNestedString(event, ["item", "text"]) ??
    readNestedString(event, ["item", "content", "0", "text"]) ??
    readNestedString(event, ["message", "delta"]) ??
    readNestedString(event, ["message", "content", "0", "text"]) ??
    readTextFromContentBlocks(event.content) ??
    readTextFromContentBlocks(event.item && typeof event.item === "object"
      ? (event.item as Record<string, unknown>).content
      : undefined);

  if (deltaText) {
    events.push({ type: "delta", text: deltaText });
  }

  const statusText =
    readNestedString(event, ["message"]) ??
    readNestedString(event, ["status"]) ??
    readNestedString(event, ["error", "message"]) ??
    readNestedString(event, ["tool_name"]) ??
    readNestedString(event, ["item", "type"]);

  if (statusText && !deltaText) {
    events.push({ type: "status", text: `${type} ${statusText}`.trim() });
  } else if (!deltaText && type) {
    events.push({ type: "status", text: type });
  }

  return events;
}

function normalizeGeminiEvent(event: Record<string, unknown>): AgentStreamEvent[] {
  const type = typeof event.type === "string" ? event.type : "";
  const events: AgentStreamEvent[] = [];

  const deltaText =
    readNestedString(event, ["content"]) ??
    readNestedString(event, ["text"]) ??
    readNestedString(event, ["delta"]) ??
    readNestedString(event, ["message", "text"]) ??
    readNestedString(event, ["candidate", "content", "parts", "0", "text"]) ??
    readTextFromContentBlocks(event.content);

  if (deltaText) {
    events.push({ type: "delta", text: deltaText });
  }

  const statusText =
    readNestedString(event, ["status"]) ??
    readNestedString(event, ["message"]) ??
    readNestedString(event, ["error", "message"]);

  if (statusText && !deltaText) {
    events.push({ type: "status", text: `${type} ${statusText}`.trim() });
  } else if (!deltaText && type) {
    events.push({ type: "status", text: type });
  }

  return events;
}

function normalizeJsonEvent(agent: AgentName, line: string): AgentStreamEvent[] {
  let parsed: unknown;

  try {
    parsed = JSON.parse(line);
  } catch {
    const text = sanitizeLine(line);
    return text ? [{ type: "delta", text }] : [];
  }

  if (!parsed || typeof parsed !== "object") {
    const text = sanitizeLine(String(parsed));
    return text ? [{ type: "delta", text }] : [];
  }

  const event = parsed as Record<string, unknown>;
  switch (agent) {
    case "claude":
      return normalizeClaudeEvent(event);
    case "codex":
      return normalizeCodexEvent(event);
    case "gemini":
      return normalizeGeminiEvent(event);
  }
}

function looksLikeLowSignalStatus(text: string): boolean {
  return (
    /^[a-z_]+(?:[.:][a-z_]+)+$/i.test(text) ||
    /^\d{4}-\d{2}-\d{2}t.*\b(error|warn|info)\b/i.test(text) ||
    /^(mcp|loaded cached credentials|scheduling mcp context refresh|executing mcp context refresh|mcp context refresh complete)/i.test(
      text,
    )
  );
}

async function runAgentCommand(
  agent: AgentName,
  command: AgentCommand,
  prompt: string,
  timeoutMs: number,
  onEvent?: (event: AgentStreamEvent) => void,
): Promise<{ content: string; durationMs: number }> {
  const start = Date.now();
  const subprocess = execa(command.cmd, command.args, {
    env: { ...process.env, NO_COLOR: "1" },
    buffer: true,
    input: prompt,
  });

  let accumulated = "";
  let stdoutBuffer = "";
  let stderrBuffer = "";
  let lastErrorStatus = "";
  let timeoutHandle: NodeJS.Timeout | null = null;
  let forceKillHandle: NodeJS.Timeout | null = null;

  const clearAgentTimeout = (): void => {
    if (timeoutHandle) {
      clearTimeout(timeoutHandle);
      timeoutHandle = null;
    }
    if (forceKillHandle) {
      clearTimeout(forceKillHandle);
      forceKillHandle = null;
    }
  };

  const resetAgentTimeout = (): void => {
    clearAgentTimeout();
    timeoutHandle = setTimeout(() => {
      subprocess.kill("SIGTERM");
      forceKillHandle = setTimeout(() => {
        subprocess.kill("SIGKILL");
      }, 1_000);
    }, timeoutMs);
  };

  const emit = (event: AgentStreamEvent): void => {
    const text = normalizeWhitespace(event.text);
    if (!text) {
      return;
    }

    if (event.type === "delta") {
      resetAgentTimeout();
      const novelText = extractNovelText(accumulated, text);
      if (!novelText) {
        return;
      }
      accumulated = appendUniqueText(accumulated, novelText);
      onEvent?.({ type: event.type, text: novelText });
      return;
    }

    if (event.type === "status" && /\berror\b/i.test(text)) {
      lastErrorStatus = text;
    }

    onEvent?.({ type: event.type, text });
  };

  const flushStdout = (flushPartial: boolean): void => {
    if (!stdoutBuffer) {
      return;
    }

    const newlinePattern = /\r?\n/g;
    let startIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = newlinePattern.exec(stdoutBuffer)) !== null) {
      const line = stdoutBuffer.slice(startIndex, match.index);
      startIndex = match.index + match[0].length;

      if (command.streamFormat === "jsonl") {
        const normalizedEvents = normalizeJsonEvent(agent, line);
        if (normalizedEvents.length > 0) {
          for (const event of normalizedEvents) {
            emit(event);
          }
        } else {
          const text = sanitizeLine(line);
          if (text) {
            emit({
              type: looksLikeLowSignalStatus(text) ? "status" : "delta",
              text: looksLikeLowSignalStatus(text) ? text : text + "\n",
            });
          }
        }
      } else {
        emit({ type: "delta", text: line + "\n" });
      }
    }

    stdoutBuffer = stdoutBuffer.slice(startIndex);

    if (flushPartial && stdoutBuffer.length > 0) {
      if (command.streamFormat === "jsonl") {
        const normalizedEvents = normalizeJsonEvent(agent, stdoutBuffer);
        if (normalizedEvents.length > 0) {
          for (const event of normalizedEvents) {
            emit(event);
          }
        } else {
          const text = sanitizeLine(stdoutBuffer);
          if (text) {
            emit({
              type: looksLikeLowSignalStatus(text) ? "status" : "delta",
              text: looksLikeLowSignalStatus(text) ? text : text + "\n",
            });
          }
        }
      } else {
        emit({ type: "delta", text: stdoutBuffer });
      }
      stdoutBuffer = "";
    }
  };

  const flushStderr = (flushPartial: boolean): void => {
    if (!stderrBuffer) {
      return;
    }

    const newlinePattern = /\r?\n/g;
    let startIndex = 0;
    let match: RegExpExecArray | null;

    while ((match = newlinePattern.exec(stderrBuffer)) !== null) {
      const line = sanitizeLine(stderrBuffer.slice(startIndex, match.index));
      startIndex = match.index + match[0].length;
      if (line) {
        emit({ type: "status", text: line });
      }
    }

    stderrBuffer = stderrBuffer.slice(startIndex);

    if (flushPartial) {
      const line = sanitizeLine(stderrBuffer);
      if (line) {
        emit({ type: "status", text: line });
      }
      stderrBuffer = "";
    }
  };

  if (subprocess.stdout) {
    subprocess.stdout.on("data", (chunk: Buffer) => {
      stdoutBuffer += stripAnsi(chunk.toString());
      flushStdout(false);
    });
  }

  if (subprocess.stderr) {
    subprocess.stderr.on("data", (chunk: Buffer) => {
      stderrBuffer += chunk.toString();
      flushStderr(false);
    });
  }

  resetAgentTimeout();

  try {
    await subprocess;
    flushStdout(true);
    flushStderr(true);
  } catch (err: unknown) {
    clearAgentTimeout();
    const message = err instanceof Error ? err.message : String(err);
    if (/timed out|terminated|killed/i.test(message) && !accumulated.trim()) {
      throw new Error(`Timed out after ${Math.round(timeoutMs / 1000)}s without receiving content.`);
    }
    throw err;
  } finally {
    clearAgentTimeout();
  }

  if (!accumulated.trim() && lastErrorStatus) {
    throw new Error(lastErrorStatus);
  }

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
  const onEvent = options.onEvent;
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
      agent,
      buildAgentCommand(agent, yoloMode),
      prompt,
      timeoutMs,
      onEvent,
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
            agent,
            fallbackCommand,
            prompt,
            timeoutMs,
            onEvent,
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
