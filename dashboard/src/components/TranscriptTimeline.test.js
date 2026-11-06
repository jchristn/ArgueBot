import { describe, expect, it } from "vitest";
import { timelineRows, rowPreview } from "./TranscriptTimeline.jsx";

const message = (id, speaker, kind, content = "x", state = "completed") => ({ id, speaker, kind, content, state, createdUtc: "2026-11-04T00:00:00Z" });

describe("timelineRows", () => {
  it("numbers rounds at orchestrator questions and groups consecutive tool calls per speaker", () => {
    const rows = timelineRows([
      message("m1", "user", "note"),
      message("m2", "Orchestrator", "question", '{"question":"Why?"}'),
      message("m3", "Codex", "tool", "Local tool: read_file\\nStatus: completed"),
      message("m4", "Codex", "tool", "Local tool: grep\\nStatus: completed"),
      message("m5", "Codex", "response"),
      message("m6", "Claude", "tool", "Local tool: ls"),
      message("m7", "Orchestrator", "question", '{"question":"Next?"}'),
    ]);
    expect(rows.map(row => row.type)).toEqual(["turn", "round", "turn", "tools", "turn", "tools", "round", "turn"]);
    expect(rows.filter(row => row.type === "round").map(row => row.round)).toEqual([1, 2]);
    expect(rows[3].messages.map(item => item.id)).toEqual(["m3", "m4"]);
    expect(rows[5].messages.map(item => item.id)).toEqual(["m6"]);
  });
  it("labels rounds with the service's round so a later page shows the real round, and skips follow-up runs", () => {
    const at = (item, runId, round) => ({ ...item, runId, round });
    const rows = timelineRows([
      at(message("m1", "Codex", "response"), "run_d", 5),
      at(message("m2", "Codex", "tool", "Conversation tool: get_debate_state"), "run_d", undefined),
      at(message("m3", "Orchestrator", "ratification", "{}"), "run_d", 5),
      at(message("m4", "Orchestrator", "question", '{"question":"Next?"}'), "run_d", 6),
      at(message("m5", "Claude", "response"), "run_d", 6),
      at(message("m6", "user", "question", "Did you agree?"), "run_f", undefined),
      at(message("m7", "Claude", "final"), "run_f", 1),
    ], new Set(["run_f"]));
    expect(rows.filter(row => row.type === "round").map(row => row.round)).toEqual([5, 6]);
    expect(rows.map(row => row.type)).toEqual(["round", "turn", "tools", "turn", "round", "turn", "turn", "turn", "turn"]);
  });
  it("shows the newest text of a streaming turn", () => {
    const preview = rowPreview(message("s", "Codex", "response", "start " + "word ".repeat(100) + "NEWEST", "streaming"));
    expect(preview.startsWith("\u2026")).toBe(true);
    expect(preview.endsWith("NEWEST")).toBe(true);
  });
});

import { votePosition } from "./TranscriptTimeline.jsx";
describe("votePosition", () => {
  it("reads a completed vote and ignores other turns", () => {
    expect(votePosition({ kind: "ratification", state: "completed", content: '{"proposalId":"p","position":"disagree","rationale":"Too risky."}' })).toEqual({ position: "disagree", rationale: "Too risky." });
    expect(votePosition({ kind: "response", state: "completed", content: "{}" })).toBe(null);
    expect(votePosition({ kind: "ratification", state: "completed", content: "not json" })).toBe(null);
  });
});

import { plainText } from "./TranscriptTimeline.jsx";
describe("plainText", () => {
  it("removes markdown markup but keeps identifiers and link text", () => {
    expect(plainText("## Findings\n\n**Bold** use `get_debate_state` and see [docs](https://example.invalid/x_y).\n\n| A | B |\n|---|---|\n| one | two |\n> quoted_value")).toBe("Findings Bold use get_debate_state and see docs. A B one two quoted_value");
  });
});
