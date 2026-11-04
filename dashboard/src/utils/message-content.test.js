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
