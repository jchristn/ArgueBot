import { describe, expect, it } from "vitest";
import { speakerHues, speakerPalette } from "./speaker-colors.js";

describe("speakerHues", () => {
  it("gives every agent a different hue, including names that used to collide", () => {
    const hues = speakerHues([], ["Claude Code", "user", "Codex", "Claude Code", "Gemini", "DeepSeek", "Mux"]);
    expect([...hues.keys()]).toEqual(["Claude Code", "Codex", "Gemini", "DeepSeek", "Mux"]);
    expect(new Set(hues.values()).size).toBe(5);
    expect(hues.has("user")).toBe(false);
  });
  it("follows roster order so colors stay stable across pages", () => {
    const hues = speakerHues(["Orchestrator", "Codex", "Claude Code"], ["Claude Code"]);
    expect(hues.get("Orchestrator")).toBe(speakerPalette[0]);
    expect(hues.get("Codex")).toBe(speakerPalette[1]);
    expect(hues.get("Claude Code")).toBe(speakerPalette[2]);
  });
  it("stays distinct beyond the palette size", () => {
    const names = Array.from({ length: 16 }, (_, index) => "Agent " + index);
    expect(new Set(speakerHues(names).values()).size).toBe(16);
  });
});
