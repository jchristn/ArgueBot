import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import "../i18n/index.js";
import i18n from "../i18n/index.js";
import MemoryPanel from "./MemoryPanel.jsx";
afterEach(cleanup);
// jsdom does not implement modal dialogs; open and close them as attributes so roles and focus queries work.
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const run = { id: "run-test", revision: 4, state: "paused", participants: [{ id: "seat-a", name: "Agent A" }] };
const question = { id: "clr-test", question: "Which rollback threshold is acceptable?", blocking: true, status: "open" };
const mockApi = () => ({
  summaries: vi.fn().mockResolvedValue([]),
  clarifications: vi.fn().mockResolvedValue([question]),
  interject: vi.fn().mockResolvedValue({ id: "inj-test" })
});

it("requires a delivery choice and text before answering a blocking clarification", async () => {
  const api = mockApi(); const onChange = vi.fn().mockResolvedValue(); const notify = vi.fn();
  render(<MemoryPanel api={api} id="conversation-test" run={run} notify={notify} onChange={onChange} />);
  expect(await screen.findByText(question.question)).toBeInTheDocument();
  expect(screen.getByText(i18n.t("memory.blocking"))).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: i18n.t("memory.answer") }));
  const dialog = await screen.findByRole("dialog");
  const send = within(dialog).getByRole("button", { name: i18n.t("memory.send") });
  const skip = within(dialog).getByRole("button", { name: i18n.t("memory.skip") });
  expect(send).toBeDisabled(); expect(skip).toBeDisabled();
  fireEvent.change(within(dialog).getByLabelText(i18n.t("memory.context")), { target: { value: "Keep the rollback threshold at five percent." } });
  expect(send).toBeDisabled();
  fireEvent.change(within(dialog).getByLabelText(i18n.t("panel.delivery")), { target: { value: "next_turn" } });
  expect(send).toBeEnabled();
  fireEvent.click(send);
  await waitFor(() => expect(api.interject).toHaveBeenCalledOnce());
  const request = api.interject.mock.calls[0][1];
  expect(request).toMatchObject({ runId: "run-test", clarificationId: "clr-test", skipClarification: false, deliveryMode: "next_turn", pinned: true, text: "Keep the rollback threshold at five percent." });
  expect(request.idempotencyKey).toMatch(/:answer$/);
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(onChange).toHaveBeenCalled();
  expect(notify).not.toHaveBeenCalled();
});

it("skips explicitly with the question recorded and keeps the dialog open on failure", async () => {
  const api = mockApi(); const notify = vi.fn();
  api.interject.mockRejectedValueOnce(Object.assign(new Error("The clarification is unavailable or was already answered."), { code: "clarification_changed" }));
  render(<MemoryPanel api={api} id="conversation-test" run={run} notify={notify} onChange={vi.fn().mockResolvedValue()} />);
  fireEvent.click(await screen.findByRole("button", { name: i18n.t("memory.answer") }));
  const dialog = await screen.findByRole("dialog");
  fireEvent.change(within(dialog).getByLabelText(i18n.t("panel.delivery")), { target: { value: "interrupt_now" } });
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("memory.skip") }));
  await waitFor(() => expect(notify).toHaveBeenCalledOnce());
  const request = api.interject.mock.calls[0][1];
  expect(request).toMatchObject({ clarificationId: "clr-test", skipClarification: true, deliveryMode: "interrupt_now" });
  expect(request.text).toContain(question.question);
  expect(request.idempotencyKey).toMatch(/:skip$/);
  expect(screen.getByRole("dialog")).toBeInTheDocument();
});
