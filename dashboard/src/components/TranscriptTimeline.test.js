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
