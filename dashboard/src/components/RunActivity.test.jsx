import { render, screen, cleanup, act } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import "../i18n/index.js";
import RunActivity from "./RunActivity.jsx";
afterEach(() => { cleanup(); vi.useRealTimers(); });
const run = { id: "run-test", state: "agent_turn", participants: [{ name: "Claude Code" }], speakerIndex: 0 };
it("shows waiting before output, streaming output, and disconnected uncertainty", () => {
  const view = render(<RunActivity run={run} messages={[]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Claude Code is working");
  expect(screen.getByRole("status")).toHaveTextContent("Waiting for agent output");
  view.rerender(<RunActivity run={run} messages={[{ runId: run.id, state: "streaming", content: "Hello" }]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Answer text has been received");
  view.rerender(<RunActivity run={run} messages={[]} connection="common.reconnecting" />);
  expect(screen.getByRole("status")).toHaveTextContent("Agent progress cannot currently be confirmed");
  expect(screen.getByRole("status")).not.toHaveTextContent("is working");
});
it("distinguishes queued and pausing work and hides inactive runs", () => {
  const view = render(<RunActivity run={{ ...run, state: "queued" }} messages={[]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Run queued");
  view.rerender(<RunActivity run={{ ...run, state: "pausing" }} messages={[]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Pause requested");
  for (const state of ["paused", "completed", "failed", "cancelled"]) {
    view.rerender(<RunActivity run={{ ...run, state }} messages={[]} connection="common.connected" />);
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  }
});

it("shows the frozen deadline and elapsed time without implying an agent heartbeat", () => {
  const createdUtc = new Date(Date.now() - 120000).toISOString();
  render(<RunActivity run={{ ...run, participants: [{ name: "Claude Code", timeoutSeconds: 300 }] }} messages={[{ runId: run.id, state: "streaming", content: "", createdUtc }]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Elapsed: 120s / per-turn limit: 300s");
  expect(screen.getByRole("status")).toHaveTextContent("not an agent heartbeat");
});

it("measures observed answer silence and resets when the full answer grows beyond its preview", () => {
  vi.useFakeTimers();
  const message = { id: "message-test", runId: run.id, state: "streaming", content: "Preview", contentBytes: 4096 };
  const view = render(<RunActivity run={run} messages={[message]} connection="common.connected" />);
  act(() => vi.advanceTimersByTime(45000));
  expect(screen.getByRole("status")).toHaveTextContent("No answer text change observed for 45s");
  view.rerender(<RunActivity run={run} messages={[{ ...message, contentBytes: 8192 }]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("No answer text change observed for 0s");
  view.rerender(<RunActivity run={run} messages={[message]} connection="common.reconnecting" />);
  expect(screen.getByRole("status")).not.toHaveTextContent("No answer text change observed");
});
