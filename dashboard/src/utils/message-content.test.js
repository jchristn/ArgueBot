import { describe, expect, it } from "vitest";
import { mergeToolActivity } from "./message-content.js";

const tool = (id, status, call, attempt = "att_1") => ({ id, kind: "tool", content: `Local tool: read_file\nStatus: ${status}\nAttempt: ${attempt}\nCall: ${call}\nArguments:\npath: a.txt` });

describe("mergeToolActivity", () => {
  it("shows one card per finished call and keeps calls that never finished", () => {
    const messages = [tool("m1", "started", "c1"), tool("m2", "started", "c2"), tool("m3", "completed", "c1"), { id: "m4", kind: "answer", content: "Status: started" }];
    expect(mergeToolActivity(messages).map(message => message.id)).toEqual(["m2", "m3", "m4"]);
  });
  it("does not merge the same call ID from different attempts", () => {
    const messages = [tool("m1", "started", "c1", "att_1"), tool("m2", "completed", "c1", "att_2")];
    expect(mergeToolActivity(messages).map(message => message.id)).toEqual(["m1", "m2"]);
  });
});

import { messageContent } from "./message-content.js";
describe("messageContent", () => {
  const show = (kind, content, extra = {}) => messageContent({ kind, content, state: "completed", ...extra });
  it("reads fenced replies, cut-off previews and streaming replies", () => {
    expect(show("synthesis", '```json\n{"proposal": "Ship behind a flag."}\n```')).toBe("Ship behind a flag.");
    expect(show("ratification", '```json\n{"proposalId":"p","position":"needs_information","rationale":"Missing data."}\n```')).toBe("**needs information**\n\nMissing data.");
    expect(show("synthesis", '{"proposal": "Line one\\nLine \\u0022two\\u0022 is cut', { contentTruncated: true })).toBe('Line one\nLine "two" is cut…');
    expect(show("question", '{"question": "Complete question.", "rationale": "cut', { contentTruncated: true })).toBe("Complete question.");
    expect(show("question", '```json\n{"question": "Still wri', { state: "streaming" })).toBe("Still wri…");
  });
  it("leaves failed turns, plain text and ordinary responses unchanged", () => {
    expect(show("question", '{"oops', { state: "failed" })).toBe('{"oops');
    expect(show("question", "Plain question?")).toBe("Plain question?");
    expect(show("response", '{"question": "x"}')).toBe('{"question": "x"}');
  });
});
