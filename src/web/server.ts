#!/usr/bin/env node

import "dotenv/config";
import { readFile, stat } from "node:fs/promises";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { isAgentInstalled, validateAgentSetup } from "../agents.js";
import type { AgentName } from "../types.js";
import { DEFAULT_CONFIG } from "../types.js";
import { DebateSession, type SessionAction, type SessionEvent } from "./session.js";

const AGENTS: { name: AgentName; label: string }[] = [
  { name: "claude", label: "Claude Code" },
  { name: "codex", label: "Codex" },
  { name: "gemini", label: "Gemini" },
];

const STATIC_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../web");
const ICON_PATH = path.resolve(STATIC_DIR, "../icon.png");
const STATIC_FILES: Record<string, { file: string; type: string }> = {
  "/": { file: path.join(STATIC_DIR, "index.html"), type: "text/html; charset=utf-8" },
  "/app.js": { file: path.join(STATIC_DIR, "app.js"), type: "text/javascript; charset=utf-8" },
  "/styles.css": { file: path.join(STATIC_DIR, "styles.css"), type: "text/css; charset=utf-8" },
  "/icon.png": { file: ICON_PATH, type: "image/png" },
};

const MAX_BODY_BYTES = 1_000_000;
const LOOPBACK_HOSTS = new Set(["localhost", "127.0.0.1", "::1", "[::1]"]);

const sessions = new Map<string, DebateSession>();

interface ServerOptions {
  host: string;
  port: number;
}

class HttpError extends Error {
  constructor(
    readonly statusCode: number,
    message: string,
  ) {
    super(message);
  }
}

function parseArgs(): ServerOptions {
  const args = process.argv.slice(2);
  const options: ServerOptions = {
    host: process.env.ARGUEBOT_HOST ?? "127.0.0.1",
    port: parseInt(process.env.ARGUEBOT_PORT ?? "4317", 10),
  };

  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case "--port":
        options.port = parseInt(args[++i], 10);
        break;
      case "--host":
        options.host = args[++i];
        break;
      case "--help":
        console.log(`
arguebot web - browser UI for multi-agent debates

Usage: npm run web -- [options]

Options:
  --port N      Port to listen on (default: 4317, env ARGUEBOT_PORT)
  --host HOST   Interface to bind (default: 127.0.0.1, env ARGUEBOT_HOST)
  --help        Show this help

There is no authentication. Anyone who can reach the server can run agent
CLIs on this machine, so keep it bound to localhost unless you trust the network.
`);
        process.exit(0);
    }
  }

  return options;
}

function isAgentName(value: unknown): value is AgentName {
  return value === "claude" || value === "codex" || value === "gemini";
}

function sendJson(res: http.ServerResponse, statusCode: number, body: unknown): void {
  res.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
  res.end(JSON.stringify(body));
}

async function readJsonBody(req: http.IncomingMessage): Promise<Record<string, unknown>> {
  if (!(req.headers["content-type"] ?? "").includes("application/json")) {
    throw new HttpError(415, "Expected application/json.");
  }

  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > MAX_BODY_BYTES) {
      throw new HttpError(413, "Request body too large.");
    }
    chunks.push(chunk as Buffer);
  }

  try {
    const parsed = JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      throw new Error();
    }
    return parsed as Record<string, unknown>;
  } catch {
    throw new HttpError(400, "Invalid JSON body.");
  }
}

/**
 * Reject requests that a malicious web page could forge against a local
 * server: foreign Host headers (DNS rebinding) and cross-origin writes.
 */
function checkRequestOrigin(req: http.IncomingMessage, options: ServerOptions): void {
  const hostHeader = req.headers.host ?? "";
  const hostname = hostHeader.replace(/:\d+$/, "");

  if (LOOPBACK_HOSTS.has(options.host) && !LOOPBACK_HOSTS.has(hostname)) {
    throw new HttpError(403, "Forbidden host.");
  }

  const origin = req.headers.origin;
  if (req.method !== "GET" && origin && origin !== `http://${hostHeader}`) {
    throw new HttpError(403, "Cross-origin requests are not allowed.");
  }
}

function listAgents() {
  return AGENTS.map((agent) => {
    const installed = isAgentInstalled(agent.name);
    return {
      ...agent,
      installed,
      setupError: installed
        ? validateAgentSetup(agent.name)
        : `The \`${agent.name}\` CLI was not found on PATH.`,
    };
  });
}

function readNumber(value: unknown, fallback: number, min: number, max: number): number {
  const n = typeof value === "number" ? value : parseFloat(String(value ?? ""));
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

async function createSession(body: Record<string, unknown>): Promise<DebateSession> {
  const topic = typeof body.topic === "string" ? body.topic.trim() : "";
  if (!topic) {
    throw new HttpError(400, "Enter a topic for the debate.");
  }

  const participants = Array.isArray(body.participants)
    ? [...new Set(body.participants.filter(isAgentName))]
    : [];
  if (participants.length < 2) {
    throw new HttpError(400, "Choose at least two agents.");
  }

  const setupErrors = participants
    .map((agent) => (isAgentInstalled(agent) ? validateAgentSetup(agent) : `The \`${agent}\` CLI was not found on PATH.`))
    .filter((message): message is string => message !== null);
  if (setupErrors.length > 0) {
    throw new HttpError(400, setupErrors.join("\n"));
  }

  let cwd = process.cwd();
  if (typeof body.cwd === "string" && body.cwd.trim()) {
    cwd = path.resolve(body.cwd.trim().replace(/^~(?=$|\/)/, process.env.HOME ?? "~"));
    const info = await stat(cwd).catch(() => null);
    if (!info?.isDirectory()) {
      throw new HttpError(400, `Working directory not found: ${cwd}`);
    }
  }

  const summaryAgent = isAgentName(body.summaryAgent) && participants.includes(body.summaryAgent)
    ? body.summaryAgent
    : participants[0];

  const id = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  const session = new DebateSession(id, topic, {
    participants,
    summaryAgent,
    cwd,
    maxRounds: Math.round(readNumber(body.maxRounds, DEFAULT_CONFIG.maxRounds, 1, 50)),
    interventionTimeoutMs:
      readNumber(body.interventionSeconds, DEFAULT_CONFIG.interventionTimeoutMs / 1000, 0, 600) * 1000,
    agentTimeoutMs: readNumber(body.agentTimeoutSeconds, DEFAULT_CONFIG.agentTimeoutMs / 1000, 10, 3600) * 1000,
    yoloMode: body.yolo === true,
  });

  sessions.set(id, session);
  session.start();
  return session;
}

function parseAction(body: Record<string, unknown>): SessionAction {
  const text = typeof body.text === "string" ? body.text : "";
  switch (body.action) {
    case "hold":
    case "release":
    case "continue":
    case "pause":
    case "resume":
    case "interrupt":
    case "stop":
      return { action: body.action };
    case "note":
    case "steer":
      return { action: body.action, text };
    case "extend":
      return { action: "extend", rounds: readNumber(body.rounds, 2, 1, 50) };
    case "followup":
      return { action: "followup", text, agent: isAgentName(body.agent) ? body.agent : undefined };
    default:
      throw new HttpError(400, `Unknown action: ${String(body.action)}`);
  }
}

function streamEvents(req: http.IncomingMessage, res: http.ServerResponse, session: DebateSession): void {
  res.writeHead(200, {
    "Content-Type": "text/event-stream; charset=utf-8",
    "Cache-Control": "no-store",
    Connection: "keep-alive",
  });

  const write = (event: SessionEvent | { type: "snapshot"; session: unknown }): void => {
    res.write(`data: ${JSON.stringify(event)}\n\n`);
  };

  write({ type: "snapshot", session: session.snapshot() });
  session.on("event", write);

  const keepAlive = setInterval(() => res.write(": keep-alive\n\n"), 15_000);
  req.on("close", () => {
    clearInterval(keepAlive);
    session.off("event", write);
  });
}

async function serveStatic(res: http.ServerResponse, pathname: string): Promise<boolean> {
  const entry = STATIC_FILES[pathname];
  if (!entry) return false;

  const content = await readFile(entry.file);
  res.writeHead(200, { "Content-Type": entry.type, "Cache-Control": "no-cache" });
  res.end(content);
  return true;
}

function getSession(id: string): DebateSession {
  const session = sessions.get(id);
  if (!session) {
    throw new HttpError(404, "Debate not found. It may have been lost when the server restarted.");
  }
  return session;
}

async function route(req: http.IncomingMessage, res: http.ServerResponse, options: ServerOptions): Promise<void> {
  checkRequestOrigin(req, options);

  const url = new URL(req.url ?? "/", "http://localhost");
  const { pathname } = url;
  const method = req.method ?? "GET";

  if (method === "GET" && (await serveStatic(res, pathname))) {
    return;
  }

  if (method === "GET" && pathname === "/api/agents") {
    sendJson(res, 200, { agents: listAgents(), cwd: process.cwd(), defaults: DEFAULT_CONFIG });
    return;
  }

  if (pathname === "/api/sessions") {
    if (method === "GET") {
      const list = [...sessions.values()]
        .map((session) => {
          const snapshot = session.snapshot();
          return {
            id: snapshot.id,
            topic: snapshot.topic,
            createdAt: snapshot.createdAt,
            participants: snapshot.config.participants,
            phase: snapshot.status.phase,
            endReason: snapshot.status.endReason,
          };
        })
        .reverse();
      sendJson(res, 200, { sessions: list });
      return;
    }
    if (method === "POST") {
      const session = await createSession(await readJsonBody(req));
      sendJson(res, 201, { id: session.id });
      return;
    }
  }

  const match = /^\/api\/sessions\/([a-z0-9]+)\/(events|actions|transcript\.md)$/.exec(pathname);
  if (match) {
    const session = getSession(match[1]);

    if (method === "GET" && match[2] === "events") {
      streamEvents(req, res, session);
      return;
    }

    if (method === "POST" && match[2] === "actions") {
      const error = session.handle(parseAction(await readJsonBody(req)));
      if (error) {
        throw new HttpError(409, error);
      }
      sendJson(res, 200, { ok: true });
      return;
    }

    if (method === "GET" && match[2] === "transcript.md") {
      const timestamp = session.createdAt.toISOString().replace(/[:.]/g, "-").slice(0, 19);
      res.writeHead(200, {
        "Content-Type": "text/markdown; charset=utf-8",
        "Content-Disposition": `attachment; filename="arguebot-transcript-${timestamp}.md"`,
      });
      res.end(session.markdown());
      return;
    }
  }

  throw new HttpError(404, "Not found.");
}

function main(): void {
  const options = parseArgs();

  const server = http.createServer((req, res) => {
    route(req, res, options).catch((err: unknown) => {
      const statusCode = err instanceof HttpError ? err.statusCode : 500;
      const message = err instanceof Error ? err.message : String(err);
      if (statusCode === 500) {
        console.error("Request failed:", err);
      }
      if (!res.headersSent) {
        sendJson(res, statusCode, { error: message });
      } else {
        res.end();
      }
    });
  });

  server.listen(options.port, options.host, () => {
    const displayHost = options.host.includes(":") ? `[${options.host}]` : options.host;
    console.log(`ArgueBot web UI running at http://${displayHost}:${options.port}`);
    if (!LOOPBACK_HOSTS.has(options.host)) {
      console.warn("Warning: bound to a non-loopback interface with no authentication.");
    }
  });
}

main();
