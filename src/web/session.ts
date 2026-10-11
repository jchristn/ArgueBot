import { EventEmitter } from "node:events";
import { callAgent } from "../agents.js";
import { checkConsensus } from "../consensus.js";
import { buildFinalQueryPrompt, buildOpeningPrompt, buildRebuttalPrompt } from "../prompts.js";
import { formatTranscriptMarkdown } from "../transcript.js";
import type { AgentName, DebateTurn } from "../types.js";

export interface WebDebateConfig {
  participants: AgentName[];
  maxRounds: number;
  interventionTimeoutMs: number;
  agentTimeoutMs: number;
  yoloMode: boolean;
  verbose: boolean;
  summaryAgent: AgentName;
  cwd: string;
}

export type SessionPhase = "agent_turn" | "window" | "paused" | "ended" | "followup";

export type EndReason = "consensus" | "max_rounds" | "stopped" | "failed";

export interface WebTurn extends DebateTurn {
  id: string;
  streaming: boolean;
  durationMs?: number;
  error?: string;
  interrupted?: boolean;
  steer?: boolean;
}

export interface SessionStatus {
  phase: SessionPhase;
  round: number;
  maxRounds: number;
  activeSpeaker: AgentName | null;
  nextSpeaker: AgentName | null;
  windowDeadline: number | null;
  windowHeld: boolean;
  endReason: EndReason | null;
  steerDirective: string | null;
  queuedNotes: string[];
  pauseRequested: boolean;
}

export interface SessionSnapshot {
  id: string;
  topic: string;
  createdAt: string;
  config: WebDebateConfig;
  status: SessionStatus;
  turns: WebTurn[];
}

export type SessionEvent =
  | { type: "status"; status: SessionStatus }
  | { type: "turn_start"; turn: WebTurn }
  | { type: "delta"; id: string; text: string }
  | { type: "turn_end"; turn: WebTurn }
  | { type: "notice"; level: "info" | "error"; message: string };

export type SessionAction =
  | { action: "hold" }
  | { action: "release" }
  | { action: "continue" }
  | { action: "note"; text: string }
  | { action: "steer"; text: string }
  | { action: "pause" }
  | { action: "resume" }
  | { action: "interrupt" }
  | { action: "stop" }
  | { action: "extend"; rounds: number }
  | { action: "followup"; text: string; agent?: AgentName };

type WindowOutcome = "continue" | "stop";

let turnCounter = 0;

/**
 * A single debate, driven by the same prompts and consensus rules as the CLI
 * orchestrator but controlled through discrete actions instead of a terminal.
 * Participants speak in a fixed order each round; every agent turn is
 * followed by an intervention window the moderator can use or let lapse.
 */
export class DebateSession extends EventEmitter {
  readonly id: string;
  readonly topic: string;
  readonly createdAt = new Date();
  readonly config: WebDebateConfig;

  private turns: WebTurn[] = [];
  private phase: SessionPhase = "agent_turn";
  private round = 1;
  private activeSpeaker: AgentName | null = null;
  private nextSpeaker: AgentName | null = null;
  private windowDeadline: number | null = null;
  private windowHeld = false;
  private endReason: EndReason | null = null;
  private steerDirective: string | null = null;
  private queuedNotes: string[] = [];
  private pauseRequested = false;
  private stopRequested = false;

  private abortController: AbortController | null = null;
  private windowTimer: NodeJS.Timeout | null = null;
  private windowResolve: ((outcome: WindowOutcome) => void) | null = null;

  constructor(id: string, topic: string, config: WebDebateConfig) {
    super();
    this.id = id;
    this.topic = topic;
    this.config = config;
  }

  start(): void {
    void this.runDebate();
  }

  snapshot(): SessionSnapshot {
    return {
      id: this.id,
      topic: this.topic,
      createdAt: this.createdAt.toISOString(),
      config: this.config,
      status: this.status(),
      turns: this.turns,
    };
  }

  markdown(): string {
    return formatTranscriptMarkdown(this.topic, this.transcript(true), this.endReason === "consensus");
  }

  /**
   * Apply a moderator action. Returns an error message when the action is
   * not valid in the current phase.
   */
  handle(input: SessionAction): string | null {
    switch (input.action) {
      case "hold":
        if (this.phase === "window" && !this.windowHeld) {
          this.clearWindowTimer();
          this.windowHeld = true;
          this.windowDeadline = null;
          this.emitStatus();
        }
        return null;

      case "release":
        if (this.phase === "window" && this.windowHeld) {
          this.windowHeld = false;
          this.armWindowTimer();
          this.emitStatus();
        }
        return null;

      case "continue":
        if (this.phase === "window" || this.phase === "paused") {
          this.resolveWindow("continue");
          return null;
        }
        return "Nothing is waiting to continue.";

      case "resume":
        if (this.phase === "paused") {
          this.resolveWindow("continue");
          return null;
        }
        if (this.pauseRequested) {
          this.pauseRequested = false;
          this.emitStatus();
          return null;
        }
        return "The debate is not paused.";

      case "note": {
        const text = input.text.trim();
        if (!text) return "Note is empty.";
        if (this.phase === "agent_turn") {
          this.queuedNotes.push(text);
          this.emitStatus();
          return null;
        }
        if (this.phase === "window" || this.phase === "paused") {
          this.addModeratorTurn(text);
          this.resolveWindow("continue");
          return null;
        }
        return "The debate has ended. Ask a follow-up question instead.";
      }

      case "steer": {
        const text = input.text.trim().replace(/^["']|["']$/g, "");
        if (!text) return "Directive is empty.";
        if (this.phase === "ended" || this.phase === "followup") {
          return "The debate has ended. Ask a follow-up question instead.";
        }
        this.steerDirective = text;
        this.addModeratorTurn(text, true);
        this.emitStatus();
        if (this.phase === "window" || this.phase === "paused") {
          this.resolveWindow("continue");
        }
        return null;
      }

      case "pause":
        if (this.phase === "window") {
          this.clearWindowTimer();
          this.phase = "paused";
          this.windowDeadline = null;
          this.emitStatus();
          return null;
        }
        if (this.phase === "agent_turn") {
          this.pauseRequested = true;
          this.emitStatus();
          return null;
        }
        return "Nothing to pause.";

      case "interrupt":
        if ((this.phase === "agent_turn" || this.phase === "followup") && this.abortController) {
          if (this.phase === "agent_turn") this.pauseRequested = true;
          this.abortController.abort();
          return null;
        }
        return "No agent is speaking.";

      case "stop":
        if (this.phase === "agent_turn") {
          this.stopRequested = true;
          this.abortController?.abort();
          return null;
        }
        if (this.phase === "window" || this.phase === "paused") {
          this.resolveWindow("stop");
          return null;
        }
        if (this.phase === "followup") {
          this.abortController?.abort();
          return null;
        }
        return "The debate has already ended.";

      case "extend": {
        const rounds = Number.isFinite(input.rounds) && input.rounds > 0 ? Math.floor(input.rounds) : 2;
        if (this.phase === "ended") {
          if (this.endReason !== "max_rounds") {
            return "Only a debate that ran out of rounds can be continued.";
          }
          this.config.maxRounds = this.round - 1 + rounds;
          this.endReason = null;
          this.notice("info", `Debate continues for ${rounds} more round${rounds === 1 ? "" : "s"}.`);
          void this.runDebate();
          return null;
        }
        if (this.phase === "followup") {
          return "Wait for the follow-up answer to finish.";
        }
        this.config.maxRounds += rounds;
        this.notice("info", `Max rounds extended to ${this.config.maxRounds}.`);
        this.emitStatus();
        return null;
      }

      case "followup": {
        const text = input.text.trim();
        if (!text) return "Question is empty.";
        if (this.phase !== "ended") {
          return this.phase === "followup"
            ? "Wait for the current answer to finish."
            : "Follow-up questions are available once the debate ends.";
        }
        const agent = input.agent && this.config.participants.includes(input.agent)
          ? input.agent
          : this.config.summaryAgent;
        void this.runFollowUp(text, agent);
        return null;
      }
    }
  }

  private status(): SessionStatus {
    return {
      phase: this.phase,
      round: this.round,
      maxRounds: this.config.maxRounds,
      activeSpeaker: this.activeSpeaker,
      nextSpeaker: this.nextSpeaker,
      windowDeadline: this.windowDeadline,
      windowHeld: this.windowHeld,
      endReason: this.endReason,
      steerDirective: this.steerDirective,
      queuedNotes: this.queuedNotes,
      pauseRequested: this.pauseRequested,
    };
  }

  private send(event: SessionEvent): void {
    this.emit("event", event);
  }

  private emitStatus(): void {
    this.send({ type: "status", status: this.status() });
  }

  private notice(level: "info" | "error", message: string): void {
    this.send({ type: "notice", level, message });
  }

  /**
   * Turns that the agents see: errored turns are excluded, and steering
   * directives are excluded because they only go to the next speaker.
   */
  private transcript(includeSteer = false): DebateTurn[] {
    return this.turns.filter(
      (turn) =>
        !turn.error &&
        turn.content.length > 0 &&
        (includeSteer || !(turn.speaker === "user" && turn.steer)),
    );
  }

  private hasAgentSpoken(): boolean {
    return this.turns.some((turn) => turn.speaker !== "user" && !turn.error && turn.content.length > 0);
  }

  private createTurn(speaker: AgentName | "user", type: DebateTurn["type"], content: string): WebTurn {
    turnCounter += 1;
    return {
      id: `t${turnCounter.toString(36)}`,
      round: this.round,
      speaker,
      content,
      timestamp: new Date(),
      type,
      streaming: false,
    };
  }

  private addModeratorTurn(text: string, steer = false): void {
    const turn = this.createTurn("user", "moderator", text);
    if (steer) turn.steer = true;
    this.turns.push(turn);
    this.send({ type: "turn_end", turn });
  }

  private opponentsOf(agent: AgentName): AgentName[] {
    const others = this.config.participants.filter((participant) => participant !== agent);
    return others.length === 1 ? [others[0]] : others;
  }

  private async runDebate(): Promise<void> {
    const { participants } = this.config;
    let failuresInRow = 0;

    while (this.round <= this.config.maxRounds) {
      for (let index = 0; index < participants.length; index += 1) {
        const speaker = participants[index];
        const isOpening = !this.hasAgentSpoken();
        const turn = await this.runAgentTurn(speaker, isOpening);

        if (this.stopRequested) {
          this.end("stopped");
          return;
        }

        if (turn.error && !turn.interrupted) {
          failuresInRow += 1;
          if (failuresInRow >= participants.length) {
            this.notice("error", "Every participant failed in a row. Ending the debate.");
            this.end("failed");
            return;
          }
        } else {
          failuresInRow = 0;
          if (!isOpening && !turn.interrupted && checkConsensus(turn.content).reached) {
            turn.type = "consensus";
            this.send({ type: "turn_end", turn });
            this.end("consensus");
            return;
          }
        }

        const isLastTurn = index === participants.length - 1;
        this.nextSpeaker = isLastTurn
          ? this.round < this.config.maxRounds ? participants[0] : null
          : participants[index + 1];

        const outcome = await this.interventionWindow();
        if (outcome === "stop") {
          this.end("stopped");
          return;
        }
      }

      this.round += 1;
    }

    this.end("max_rounds");
  }

  private async runAgentTurn(agent: AgentName, isOpening: boolean): Promise<WebTurn> {
    let prompt: string;
    let steered = false;
    if (isOpening) {
      prompt = buildOpeningPrompt(this.topic, agent, this.opponentsOf(agent));
    } else {
      steered = this.steerDirective !== null;
      prompt = buildRebuttalPrompt(
        this.topic,
        this.transcript(),
        agent,
        this.opponentsOf(agent),
        this.steerDirective,
      );
      this.steerDirective = null;
    }

    const turn = this.createTurn(agent, isOpening ? "opening" : "rebuttal", "");
    turn.streaming = true;
    if (steered) turn.steer = true;
    this.turns.push(turn);

    this.phase = "agent_turn";
    this.activeSpeaker = agent;
    this.nextSpeaker = null;
    this.send({ type: "turn_start", turn });
    this.emitStatus();

    await this.streamAgent(turn, agent, prompt);

    this.activeSpeaker = null;
    return turn;
  }

  private async streamAgent(turn: WebTurn, agent: AgentName, prompt: string): Promise<void> {
    const controller = new AbortController();
    this.abortController = controller;

    const result = await callAgent(agent, prompt, {
      timeoutMs: this.config.agentTimeoutMs,
      yoloMode: this.config.yoloMode,
      cwd: this.config.cwd,
      signal: controller.signal,
      onEvent: (event) => {
        if (event.type === "delta") {
          turn.content += event.text;
          this.send({ type: "delta", id: turn.id, text: event.text });
        } else if (this.config.verbose) {
          this.notice("info", `${agent}: ${event.text}`);
        }
      },
    });

    this.abortController = null;
    turn.streaming = false;
    turn.durationMs = result.durationMs;

    if (controller.signal.aborted) {
      turn.interrupted = true;
      turn.content = turn.content.trim();
      if (turn.content) {
        turn.content += "\n\n[Interrupted by the moderator]";
      } else {
        turn.error = "Interrupted before responding.";
      }
    } else if (result.error) {
      turn.error = result.error;
      turn.content = "";
    } else {
      turn.content = result.content;
    }

    this.send({ type: "turn_end", turn });
  }

  private interventionWindow(): Promise<WindowOutcome> {
    for (const note of this.queuedNotes.splice(0)) {
      this.addModeratorTurn(note);
    }

    this.windowHeld = false;
    if (this.pauseRequested) {
      this.pauseRequested = false;
      this.phase = "paused";
      this.windowDeadline = null;
    } else {
      this.phase = "window";
      this.armWindowTimer();
    }
    this.emitStatus();

    return new Promise((resolve) => {
      this.windowResolve = resolve;
    });
  }

  private armWindowTimer(): void {
    this.clearWindowTimer();
    this.windowDeadline = Date.now() + this.config.interventionTimeoutMs;
    this.windowTimer = setTimeout(() => this.resolveWindow("continue"), this.config.interventionTimeoutMs);
  }

  private clearWindowTimer(): void {
    if (this.windowTimer) {
      clearTimeout(this.windowTimer);
      this.windowTimer = null;
    }
  }

  private resolveWindow(outcome: WindowOutcome): void {
    this.clearWindowTimer();
    this.windowDeadline = null;
    this.windowHeld = false;
    const resolve = this.windowResolve;
    this.windowResolve = null;
    resolve?.(outcome);
  }

  private end(reason: EndReason): void {
    this.phase = "ended";
    this.endReason = reason;
    this.activeSpeaker = null;
    this.nextSpeaker = null;
    this.windowDeadline = null;
    this.pauseRequested = false;
    this.stopRequested = false;
    for (const note of this.queuedNotes.splice(0)) {
      this.addModeratorTurn(note);
    }
    this.emitStatus();
  }

  private async runFollowUp(question: string, agent: AgentName): Promise<void> {
    this.phase = "followup";
    this.activeSpeaker = agent;
    const asked = this.createTurn("user", "final", question);
    this.turns.push(asked);
    this.send({ type: "turn_end", turn: asked });

    const prompt = buildFinalQueryPrompt(this.topic, this.transcript(), question, agent);
    const turn = this.createTurn(agent, "final", "");
    turn.streaming = true;
    this.turns.push(turn);
    this.send({ type: "turn_start", turn });
    this.emitStatus();

    await this.streamAgent(turn, agent, prompt);

    this.phase = "ended";
    this.activeSpeaker = null;
    this.emitStatus();
  }
}
