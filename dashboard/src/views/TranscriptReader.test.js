import { describe, expect, it } from "vitest";
import { readerBlocks, readerRounds } from "./TranscriptReader.jsx";

const m = (id, speaker, kind) => ({ id, speaker, kind, content: "{}", state: "completed" });

describe("transcript reader grouping", () => {
  it("starts a round at each orchestrator question and keeps the preface separate", () => {
    const rounds = readerRounds([m("1", "user", "note"), m("2", "Orc", "question"), m("3", "A", "response"), m("4", "Orc", "question"), m("5", "B", "response")]);
    expect(rounds.map(round => [round.round, round.items.map(item => item.id)])).toEqual([[0, ["1"]], [1, ["2", "3"]], [2, ["4", "5"]]]);
  });
  it("folds one speaker's consecutive tool calls and a run of votes into single blocks", () => {
    const blocks = readerBlocks([m("1", "A", "tool"), m("2", "A", "tool"), m("3", "B", "tool"), m("4", "A", "response"), m("5", "A", "ratification"), m("6", "B", "ratification"), m("7", "C", "ratification")]);
    expect(blocks.map(block => [block.type, (block.messages || [block.message]).map(item => item.id)])).toEqual([["tools", ["1", "2"]], ["tools", ["3"]], ["turn", ["4"]], ["votes", ["5", "6", "7"]]]);
  });
});
