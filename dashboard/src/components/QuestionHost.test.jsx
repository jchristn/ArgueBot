import { render, screen, cleanup, fireEvent, waitFor, within, act } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import i18n from "../i18n/index.js";
import QuestionHost, { useQuestions } from "./QuestionHost.jsx";
import WaitingBanner from "./WaitingBanner.jsx";
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };
beforeEach(() => { document.title = "ArgueBot"; vi.useFakeTimers({ shouldAdvanceTime: true }); });
afterEach(() => { cleanup(); vi.useRealTimers(); document.title = "ArgueBot"; });

const request = (id, conversationId, extra = {}) => ({ id, conversationId, conversationTitle: "Deploys", runId: "run_a", askerId: "seat", askerName: "Planner", context: "", wait: true, status: "open", holdState: "held", createdUtc: "2026-11-09T10:00:00Z", questions: [{ id: id + "_q", key: "ok", header: "Approve", prompt: "Go ahead?", kind: "confirm", options: [], required: true, minSelections: 0, maxSelections: 0, maxLength: 0, answer: null }], ...extra });

function fakeApi(open) {
  let served = false;
  return {
    questionSummary: vi.fn().mockResolvedValue({ openMine: open.length, openTenant: open.length, waitingRuns: open.length, newestId: open[0]?.id || "" }),
    questionEvents: vi.fn().mockImplementation(async after => {
      if (!after) return { items: [], next: "c0" };
      if (served) return { items: [], next: "c1" };
      served = true;
      return { items: open.map(item => ({ requestId: item.id, conversationId: item.conversationId, status: "opened", cursor: "c1" })), next: "c1" };
    }),
    question: vi.fn().mockImplementation(async id => open.find(item => item.id === id)),
    questions: vi.fn().mockImplementation(async ({ conversationId }) => ({ items: open.filter(item => !conversationId || item.conversationId === conversationId), totalCount: open.length, pageNumber: 1, pageSize: 25 })),
    runQuestions: vi.fn().mockResolvedValue(open),
  };
}
function Count() { const { summary } = useQuestions(); return <span data-testid="count">{summary?.openMine ?? "-"}</span>; }
function mount(api, path) {
  return render(<MemoryRouter initialEntries={[path]}><QuestionHost api={api}><Routes>
    <Route path="/dashboard/conversations/:id" element={<><Count /><WaitingBanner api={api} conversationId="cnv_a" run={{ id: "run_a", state: "awaiting_input", revision: 1 }} /></>} />
    <Route path="/dashboard/home" element={<Count />} />
  </Routes></QuestionHost></MemoryRouter>);
}

it("opens the question dialog by itself on the conversation that is waiting", async () => {
  const api = fakeApi([request("qrq_a", "cnv_a")]);
  mount(api, "/dashboard/conversations/cnv_a");
  const dialog = await screen.findByRole("dialog");
  expect(await within(dialog).findByText("Go ahead?")).toBeInTheDocument();
  await waitFor(() => expect(document.title).toBe("(1) ArgueBot"));
});

it("shows a toast elsewhere, and its Answer button opens the dialog", async () => {
  const api = fakeApi([request("qrq_b", "cnv_b")]);
  mount(api, "/dashboard/home");
  await act(async () => { await vi.advanceTimersByTimeAsync(5500); });
  const toast = await screen.findByText(i18n.t("questions.toast", { name: "Planner", title: "Deploys" }));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  fireEvent.click(within(toast.closest(".question-toast")).getByRole("button", { name: i18n.t("questions.answer") }));
  expect(await screen.findByRole("dialog")).toBeInTheDocument();
});

it("the waiting banner offers only an Answer button that opens the dialog; nothing is answered inline", async () => {
  const api = fakeApi([request("qrq_a", "cnv_a")]);
  mount(api, "/dashboard/conversations/cnv_a");
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("common.close") }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  const banner = await screen.findByRole("status");
  expect(within(banner).queryByRole("radio")).not.toBeInTheDocument();
  expect(within(banner).queryByRole("textbox")).not.toBeInTheDocument();
  fireEvent.click(within(banner).getByRole("button", { name: i18n.t("questions.answer") }));
  expect(await screen.findByRole("dialog")).toBeInTheDocument();
});
