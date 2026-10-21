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
