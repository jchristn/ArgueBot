import { render, screen, cleanup } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import "../i18n/index.js";
import RunActivity from "./RunActivity.jsx";
afterEach(cleanup);
const run = { id: "run-test", state: "agent_turn", participants: [{ name: "Claude Code" }], speakerIndex: 0 };
it("shows waiting before output, streaming output, and disconnected uncertainty", () => {
  const view = render(<RunActivity run={run} messages={[]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Claude Code is working");
  expect(screen.getByRole("status")).toHaveTextContent("Waiting for agent output");
  view.rerender(<RunActivity run={run} messages={[{ runId: run.id, state: "streaming", content: "Hello" }]} connection="common.connected" />);
  expect(screen.getByRole("status")).toHaveTextContent("Receiving agent output");
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
