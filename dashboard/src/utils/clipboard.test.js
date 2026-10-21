import { afterEach, describe, expect, it, vi } from "vitest";
import { copyText } from "./clipboard.js";
import { randomId } from "./random-id.js";
afterEach(() => { vi.restoreAllMocks(); document.body.innerHTML = ""; });
describe("clipboard across browser security contexts", () => {
  it("uses the modern clipboard on a permitted secure origin", async () => {
    const writeText = vi.fn().mockResolvedValue();
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    expect(await copyText("A turn 議論")).toBe(true);
    expect(writeText).toHaveBeenCalledWith("A turn 議論");
    vi.unstubAllGlobals();
  });
  it("falls back inside an open dialog and restores selection/focus when clipboard is missing or denied", async () => {
    for (const clipboard of [undefined, { writeText: vi.fn().mockRejectedValue(new Error("denied")) }]) {
      const dialog = document.createElement("dialog"); dialog.setAttribute("open", "");
      const input = document.createElement("input"); input.value = "draft survives"; dialog.append(input); document.body.append(dialog);
      input.focus(); input.setSelectionRange(2, 5);
      vi.stubGlobal("navigator", { clipboard });
      Object.defineProperty(document, "execCommand", { configurable: true, value: vi.fn(() => {
        const field = dialog.querySelector("textarea"); expect(field.value).toBe("Full transcript\n議論"); expect(document.activeElement).toBe(field); return true;
      }) });
      expect(await copyText("Full transcript\n議論")).toBe(true);
      expect(document.activeElement).toBe(input); expect(input.selectionStart).toBe(2); expect(input.selectionEnd).toBe(5);
      expect(dialog.querySelector("textarea")).toBeNull(); dialog.remove(); vi.unstubAllGlobals();
    }
  });
  it("reports failure instead of a false success when browser copying is blocked", async () => {
    vi.stubGlobal("navigator", {});
    Object.defineProperty(document, "execCommand", { configurable: true, value: () => false });
    expect(await copyText("manual fallback")).toBe(false); vi.unstubAllGlobals();
  });
  it("creates UUIDs on insecure HTTP without randomUUID", () => {
    vi.stubGlobal("crypto", { getRandomValues: values => values.fill(7) });
    expect(randomId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/); vi.unstubAllGlobals();
  });
});
