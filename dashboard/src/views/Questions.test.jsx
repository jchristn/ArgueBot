import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import i18n from "../i18n/index.js";
import QuestionHost from "../components/QuestionHost.jsx";
import Questions from "./Questions.jsx";
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };
afterEach(cleanup);

const item = (id, status, extra = {}) => ({ id, conversationId: "cnv_a", conversationTitle: "Deploys", runId: "run_a", askerId: "seat", askerName: "Planner", context: "", wait: true, status, holdState: status === "open" ? "held" : "none", createdUtc: new Date(Date.now() - 120000).toISOString(), resolutionNote: "", questions: [{ id: id + "_q", key: "env", header: "Environment", prompt: "Which environment?", kind: "single_choice", options: [{ id: "o1", key: "prod", label: "Production" }], allowOther: false, required: true, minSelections: 1, maxSelections: 1, maxLength: 0, answer: status === "answered" ? { skipped: false, selected: ["prod"], selectedLabels: ["Production"], otherText: "", text: "", confirmed: null } : null }], ...extra });
function OutletHost({ api }) { return <Outlet context={{ api, notify: vi.fn() }} />; }
function renderPage(api) {
  return render(<MemoryRouter initialEntries={["/dashboard/questions"]}><QuestionHost api={api}><Routes><Route element={<OutletHost api={api} />}><Route path="/dashboard/questions" element={<Questions />} /></Route></Routes></QuestionHost></MemoryRouter>);
}
const baseApi = items => ({
  questions: vi.fn().mockImplementation(async ({ status }) => { const rows = items.filter(row => !status || (status === "resolved" ? row.status !== "open" : row.status === status)); return { items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 25 }; }),
  questionSummary: vi.fn().mockResolvedValue({ openMine: 1, openTenant: 1, waitingRuns: 1, newestId: "qrq_open" }),
  questionEvents: vi.fn().mockResolvedValue({ items: [], next: "c0" }),
  question: vi.fn().mockImplementation(async id => items.find(row => row.id === id)),
});

it("lists open questions first with the toolbar summary, and a row opens the question dialog", async () => {
  const api = baseApi([item("qrq_open", "open"), item("qrq_done", "answered")]);
  renderPage(api);
  expect(await screen.findByText("Which environment?")).toBeInTheDocument();
  expect(screen.getByText(i18n.t("questions.showing", { from: 1, to: 1, total: 1, page: 1, pages: 1 }))).toBeInTheDocument();
  expect(api.questions).toHaveBeenCalledWith(expect.objectContaining({ status: "open", scope: "mine", pageNumber: 1 }));
  fireEvent.click(screen.getByText("Planner"));
  const dialog = await screen.findByRole("dialog");
  expect(within(dialog).getByText(i18n.t("questions.modalTitle"))).toBeInTheDocument();
});

it("filters by status and scope, resetting to page 1, and shows resolved answers read-only", async () => {
  const api = baseApi([item("qrq_open", "open"), item("qrq_done", "answered")]);
  renderPage(api);
  await screen.findByText("Which environment?");
  fireEvent.change(screen.getByLabelText(i18n.t("questions.statusFilter")), { target: { value: "resolved" } });
  fireEvent.change(screen.getByLabelText(i18n.t("questions.scope")), { target: { value: "tenant" } });
  await waitFor(() => expect(api.questions).toHaveBeenLastCalledWith(expect.objectContaining({ status: "resolved", scope: "tenant", pageNumber: 1 })));
  fireEvent.click(await screen.findByText("Planner"));
  const dialog = await screen.findByRole("dialog");
  expect(within(dialog).getByText("Production")).toBeInTheDocument();
  expect(within(dialog).queryByRole("radio")).not.toBeInTheDocument();
});

it("explains an empty inbox", async () => {
  renderPage(baseApi([]));
  expect(await screen.findByText(i18n.t("questions.emptyOpen"))).toBeInTheDocument();
});

it("selects resolved questions of finished debates and deletes them; open ones and ones from running debates say why they cannot be deleted", async () => {
  const rows = [item("qrq_open", "open"), item("qrq_running", "answered", { deletable: false, runState: "agent_turn", conversationTitle: "Running" }), item("qrq_done", "answered", { deletable: true, runState: "completed", conversationTitle: "Finished" })];
  const api = { ...baseApi(rows), deleteQuestion: vi.fn().mockResolvedValue({ accepted: true }) };
  renderPage(api);
  fireEvent.change(await screen.findByLabelText(i18n.t("questions.statusFilter")), { target: { value: "" } });
  await waitFor(() => expect(screen.getAllByRole("checkbox")).toHaveLength(4));
  const [, open, running, done] = screen.getAllByRole("checkbox");
  expect(open).toBeDisabled();
  expect(open.closest("td")).toHaveAttribute("title", i18n.t("bulk.blocked.questionOpen"));
  expect(running).toBeDisabled();
  expect(running.closest("td")).toHaveAttribute("title", i18n.t("bulk.blocked.questionRunning"));
  fireEvent.click(done);
  fireEvent.click(screen.getByRole("button", { name: /Delete multiple \(1\)/ }));
  const dialog = await screen.findByRole("dialog");
  expect(dialog).toHaveTextContent("Finished");
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("bulk.deleteCount", { count: 1 }) }));
  await waitFor(() => expect(api.deleteQuestion).toHaveBeenCalledWith("qrq_done"));
  expect(api.deleteQuestion).toHaveBeenCalledTimes(1);
});
