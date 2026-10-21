import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient } from "./client.js";

const encode = new TextEncoder();
function frame(sequence, conversationId = "cnv_fixture") {
  return `id: ${conversationId}:${sequence}\r\nevent: change\r\ndata: ${JSON.stringify({ conversationId, sequence, kind: "delta", text: "議論🙂" })}\r\n\r\n`;
}
function response(chunks, cancel = vi.fn()) {
  return new Response(new ReadableStream({ start(controller) { for (const chunk of chunks) controller.enqueue(typeof chunk === "string" ? encode.encode(chunk) : chunk); controller.close(); }, cancel }), { status: 200, headers: { "Content-Type": "text/event-stream" } });
}
afterEach(() => vi.unstubAllGlobals());
describe("durable event reader", () => {
  it("decodes split UTF-8 and CRLF, deduplicating replayed identifiers", async () => {
    const bytes = encode.encode(frame(1) + frame(1) + frame(2));
    const chunks = Array.from(bytes, (value) => new Uint8Array([value]));
    vi.stubGlobal("fetch", vi.fn(async () => response(chunks)));
    const changes = [];
    await new ApiClient().events("cnv_fixture", "", (data, id) => changes.push([data.text, id]), new AbortController().signal);
    expect(changes).toEqual([["議論🙂", "cnv_fixture:1"], ["議論🙂", "cnv_fixture:2"]]);
  });
  it("accepts carriage-return-only SSE framing", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response([frame(1).replace(/\r\n/g, "\r")])));
    const callback = vi.fn();
    await new ApiClient().events("cnv_fixture", "", callback, new AbortController().signal);
    expect(callback).toHaveBeenCalledTimes(1);
  });
  it("rejects a missing committed sequence so callers reload a snapshot", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response([frame(3)])));
    await expect(new ApiClient().events("cnv_fixture", "cnv_fixture:1", vi.fn(), new AbortController().signal)).rejects.toMatchObject({ status: 409, code: "event_history_gap" });
  });
  it("does not apply a foreign-conversation frame", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response([frame(1, "cnv_other")])));
    const callback = vi.fn();
    await expect(new ApiClient().events("cnv_fixture", "", callback, new AbortController().signal)).rejects.toMatchObject({ code: "invalid_event" });
    expect(callback).not.toHaveBeenCalled();
  });
  it("surfaces a replay-gap error sent after SSE headers", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response(['event: error\ndata: {"code":"event_history_gap","message":"Refresh snapshot."}\n\n'])));
    await expect(new ApiClient().events("cnv_fixture", "", vi.fn(), new AbortController().signal)).rejects.toMatchObject({ status: 409, code: "event_history_gap" });
  });
  it("limits an unterminated frame instead of buffering indefinitely", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response(["data: " + "x".repeat(1048576)])));
    await expect(new ApiClient().events("cnv_fixture", "", vi.fn(), new AbortController().signal)).rejects.toMatchObject({ code: "event_frame_too_large" });
  });
  it("ignores obsolete identity events without requiring a sign-in", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response(["event: session_revoked\ndata: {}\n\n"])));
    await expect(new ApiClient().events("cnv_fixture", "", vi.fn(), new AbortController().signal)).resolves.toBeUndefined();
  });
});
