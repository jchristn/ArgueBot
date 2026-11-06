import { describe, expect, it } from "vitest";
import { byteLength, formatSize, lineDiff, parseTags, resourceToolLink, validResourceName } from "./resources.js";

describe("resource helpers", () => {
  it("normalizes tags and validates names like the service", () => {
    expect(parseTags(" Finance, core,finance,, ")).toEqual(["finance", "core"]);
    expect(validResourceName("Team/Conventions")).toBe(true);
    for (const name of ["", "/a", "a/", "a//b", "a/../b", ".x", "-x", "a b", "x".repeat(129)]) expect(validResourceName(name)).toBe(false);
    expect(byteLength("é")).toBe(2);
    expect(formatSize(512)).toBe("512 B");
    expect(formatSize(2048)).toBe("2 KiB");
  });
  it("diffs lines with additions and removals in order", () => {
    expect(lineDiff("a\nb\nc", "a\nc\nd")).toEqual([
      { kind: "same", text: "a" }, { kind: "removed", text: "b" }, { kind: "same", text: "c" }, { kind: "added", text: "d" },
    ]);
    expect(lineDiff("same", "same")).toEqual([{ kind: "same", text: "same" }]);
  });
  it("links completed, successful resource tool messages to the revision involved", () => {
    const message = "Conversation tool: append_resource\nStatus: completed\nAttempt: att_1\nCall: c1\nArguments:\nname: debate-notes\ntext: Hi\nResult:\nsuccess: true\nresult:\n  name: debate-notes\n  revision: 4\n  characters: 20\n  locked: false\n";
    expect(resourceToolLink(message)).toEqual({ tool: "append_resource", name: "debate-notes", revision: 4 });
    expect(resourceToolLink(message.replace("success: true", "success: false"))).toBeNull();
    expect(resourceToolLink(message.replace("Status: completed", "Status: started"))).toBeNull();
    expect(resourceToolLink("Conversation tool: get_turn\nStatus: completed\nResult:\nsuccess: true\nresult:\n  name: x\n  revision: 1\n")).toBeNull();
  });
});
