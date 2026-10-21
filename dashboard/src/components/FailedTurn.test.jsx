import { render, screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import "../i18n/index.js";
import FailedTurn from "./FailedTurn.jsx";
afterEach(cleanup);
it("annotates the matching turn with its own error across attempt pages", async () => {
  const api = { attempts: vi.fn()
    .mockResolvedValueOnce({ items: [{ messageId: "other", errorCode: "wrong" }], pageSize: 1, totalCount: 2 })
    .mockResolvedValueOnce({ items: [{ messageId: "message-test", errorCode: "InvalidDataException" }], pageSize: 1, totalCount: 2 }) };
  const inspect = vi.fn();
  render(<FailedTurn api={api} conversationId="conversation-test" message={{ id: "message-test", runId: "run-test" }} onInspect={inspect} />);
  expect(screen.getByRole("alert")).toHaveTextContent("This turn failed");
  await screen.findByText("InvalidDataException");
  expect(screen.getByRole("alert")).not.toHaveTextContent("wrong");
  expect(api.attempts).toHaveBeenLastCalledWith("conversation-test", "run-test", 2, 100);
  fireEvent.click(screen.getByRole("button", { name: "Generation attempts" }));
  expect(inspect).toHaveBeenCalledOnce();
});
it("preserves a visible failure when details are unavailable", async () => {
  const api = { attempts: vi.fn().mockRejectedValue(new Error("offline")) };
  render(<FailedTurn api={api} conversationId="conversation-test" message={{ id: "message-test", runId: "run-test" }} onInspect={() => {}} />);
  await waitFor(() => expect(api.attempts).toHaveBeenCalledOnce());
  expect(screen.getByRole("alert")).toHaveTextContent("This turn failed");
});

it("shows the I/O cause and recovery directly beside a failed turn", async () => {
  const errorMessage = "Agent standard output exceeded this run's output byte limit. CLI protocol events and tool results count toward this limit. Increase Output byte limit in Agents, then stop this run and start a new debate. Resume keeps the original limit.";
  const api = { attempts: vi.fn().mockResolvedValue({ items: [{ messageId: "message-test", errorCode: "agent_stdout_limit", errorMessage }], pageSize: 25, totalCount: 1 }) };
  render(<FailedTurn api={api} conversationId="conversation-test" message={{ id: "message-test", runId: "run-test", content: "Partial answer" }} onInspect={() => {}} />);
  await screen.findByText(errorMessage);
  expect(screen.getByRole("alert")).toHaveTextContent("tool results count");
  expect(screen.getByRole("alert")).toHaveTextContent("Resume keeps the original limit");
});
