import { describe, expect, it } from "vitest";
import { promptProblems } from "./PromptEditor.jsx";

const parameters = [
  { name: "question", purposes: ["question", "response"], requiredFor: [] },
  { name: "current_question", purposes: ["response"], requiredFor: ["response"] },
  { name: "proposal_id", purposes: ["ratification"], requiredFor: ["ratification"] },
];

describe("promptProblems", () => {
  it("flags unknown, unavailable and missing required parameters once each", () => {
    expect(promptProblems("{question} {context} {context} {proposal_id}", "response", parameters)).toEqual([
      { kind: "unknown", name: "context" },
      { kind: "unavailable", name: "proposal_id" },
      { kind: "missing", name: "current_question" },
    ]);
  });
  it("accepts doubled braces and a complete prompt", () => {
    expect(promptProblems("Return {{\"answer\": 1}} for {current_question}", "response", parameters)).toEqual([]);
  });
});
