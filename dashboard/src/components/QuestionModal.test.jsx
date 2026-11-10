import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "../i18n/index.js";
import QuestionModal, { answerBody, validateAnswers } from "./QuestionModal.jsx";
afterEach(cleanup);
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const option = (key, label, extra = {}) => ({ id: "qop_" + key, key, label, description: "", recommended: false, ...extra });
const request = (extra = {}) => ({
  id: "qrq_test", conversationId: "cnv_test", conversationTitle: "Deploys", runId: "run_test", askerId: "seat_a", askerName: "Planner",
  context: "The answer decides the rollback design.", wait: true, visibility: "panel", status: "open", holdState: "held", credentialWarning: false,
  createdUtc: "2026-11-09T10:00:00Z", resolvedUtc: null, resolutionNote: "",
  questions: [
    { id: "qst_env", key: "environment", header: "Environment", prompt: "Which environment?", kind: "single_choice", options: [option("prod", "Production", { recommended: true }), option("all", "Every environment")], allowOther: true, minSelections: 1, maxSelections: 1, maxLength: 0, placeholder: "", required: true, answer: null },
    { id: "qst_pain", key: "pain", header: "Failures", prompt: "Which failures hurt?", kind: "multi_choice", options: [option("migrations", "Migrations"), option("config", "Configuration drift"), option("capacity", "Capacity")], allowOther: false, minSelections: 1, maxSelections: 2, maxLength: 0, placeholder: "", required: true, answer: null },
    { id: "qst_window", key: "window", header: "Rollback", prompt: "Acceptable rollback window?", kind: "text", options: [], allowOther: false, minSelections: 0, maxSelections: 0, maxLength: 50, placeholder: "", required: false, answer: null },
    { id: "qst_ok", key: "ok", header: "Approve", prompt: "May the panel assume a staging copy exists?", kind: "confirm", options: [], allowOther: false, minSelections: 0, maxSelections: 0, maxLength: 0, placeholder: "", required: true, answer: null },
  ],
  ...extra,
});

it("answers every kind of question in the dialog, validating before it sends", async () => {
  const api = { question: vi.fn().mockResolvedValue(request()), answerQuestion: vi.fn().mockImplementation(async (id, body) => ({ ...request(), status: "answered", body })) };
  const onResolved = vi.fn();
  render(<QuestionModal api={api} requestId="qrq_test" onClose={vi.fn()} onResolved={onResolved} />);
  const dialog = await screen.findByRole("dialog");
  expect(within(dialog).getByText(i18n.t("questions.recommended"))).toBeInTheDocument();
  expect(within(dialog).getByText(i18n.t("questions.secretNotice"))).toBeInTheDocument();
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.submit") }));
  expect(await within(dialog).findByText(i18n.t("questions.error.summary", { count: 3 }))).toBeInTheDocument();
  expect(api.answerQuestion).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByLabelText(/^Production/));
  fireEvent.click(within(dialog).getByLabelText("Migrations"));
  fireEvent.click(within(dialog).getByLabelText("Configuration drift"));
  fireEvent.click(within(dialog).getByLabelText("Capacity"));
  fireEvent.click(within(dialog).getByLabelText(i18n.t("common.no")));
  fireEvent.change(within(dialog).getByLabelText("Rollback"), { target: { value: "Fifteen minutes" } });
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.submit") }));
  expect(await within(dialog).findByText(i18n.t("questions.error.multi", { min: 1, max: 2 }))).toBeInTheDocument();
  fireEvent.click(within(dialog).getByLabelText("Capacity"));
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.submit") }));
  await waitFor(() => expect(api.answerQuestion).toHaveBeenCalledOnce());
  const [, body] = api.answerQuestion.mock.calls[0];
  expect(body.answers).toEqual([
    { questionId: "qst_env", selected: ["prod"], otherText: "", text: "", confirmed: null },
    { questionId: "qst_pain", selected: ["migrations", "config"], otherText: "", text: "", confirmed: null },
    { questionId: "qst_window", selected: [], otherText: "", text: "Fifteen minutes", confirmed: null },
    { questionId: "qst_ok", selected: [], otherText: "", text: "", confirmed: false },
  ]);
  expect(body.idempotencyKey).toBeTruthy();
  expect(onResolved).toHaveBeenCalledOnce();
});

it("closing never skips; Answer later ends the waiting turn; skipping sends the note", async () => {
  const api = { question: vi.fn().mockResolvedValue(request()), deferQuestion: vi.fn().mockResolvedValue(request({ holdState: "parked" })), skipQuestion: vi.fn().mockResolvedValue(request({ status: "skipped" })) };
  const onClose = vi.fn();
  const { unmount } = render(<QuestionModal api={api} requestId="qrq_test" onClose={onClose} />);
  let dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("common.close") }));
  expect(onClose).toHaveBeenCalledOnce();
  expect(api.skipQuestion).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.later") }));
  await waitFor(() => expect(api.deferQuestion).toHaveBeenCalledWith("qrq_test"));
  unmount();
  render(<QuestionModal api={api} requestId="qrq_test" onClose={vi.fn()} />);
  dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.skip") }));
  fireEvent.change(within(dialog).getByLabelText(i18n.t("questions.note")), { target: { value: "Assume production only." } });
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.skipConfirm") }));
  await waitFor(() => expect(api.skipQuestion).toHaveBeenCalledOnce());
  expect(api.skipQuestion.mock.calls[0][1].note).toBe("Assume production only.");
  expect(await within(dialog).findByText(i18n.t("questions.status.skipped"))).toBeInTheDocument();
});

it("switches to a read-only view when someone else answered first", async () => {
  const answered = request({ status: "answered", resolvedUtc: "2026-11-09T10:01:00Z", questions: request().questions.map(question => ({ ...question, answer: { skipped: false, selected: [], selectedLabels: question.kind === "single_choice" ? ["Production"] : [], otherText: "", text: "", confirmed: question.kind === "confirm" ? true : null } })) });
  const api = { question: vi.fn().mockResolvedValueOnce(request()).mockResolvedValue(answered), answerQuestion: vi.fn().mockRejectedValue(Object.assign(new Error("This question was already answered."), { code: "question_already_resolved" })) };
  render(<QuestionModal api={api} requestId="qrq_test" onClose={vi.fn()} />);
  const dialog = await screen.findByRole("dialog");
  fireEvent.click(within(dialog).getByLabelText(/^Production/));
  fireEvent.click(within(dialog).getByLabelText("Migrations"));
  fireEvent.click(within(dialog).getByLabelText(i18n.t("common.yes")));
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("questions.submit") }));
  expect(await within(dialog).findByText(i18n.t("questions.resolvedElsewhere"))).toBeInTheDocument();
  expect(within(dialog).getByText(i18n.t("questions.status.answered"))).toBeInTheDocument();
  expect(within(dialog).queryByRole("button", { name: i18n.t("questions.submit") })).not.toBeInTheDocument();
});

it("warns strongly when the question reads like a request for a secret", async () => {
  const api = { question: vi.fn().mockResolvedValue(request({ credentialWarning: true })) };
  render(<QuestionModal api={api} requestId="qrq_test" onClose={vi.fn()} />);
  expect(await screen.findByText(i18n.t("questions.credentialWarning"))).toBeInTheDocument();
});

it("requires text for Other and keeps the body shape the service expects", () => {
  const value = request();
  const answers = Object.fromEntries(value.questions.map(question => [question.id, { selected: [], other: "", otherOn: false, text: "", confirmed: null, skip: false }]));
  answers.qst_env = { ...answers.qst_env, otherOn: true, other: "" };
  expect(validateAnswers(value, answers, i18n.t).qst_env).toBe(i18n.t("questions.error.other"));
  answers.qst_env.other = "Canary only";
  answers.qst_window.skip = true;
  expect(answerBody(value, answers, "key").answers[0]).toEqual({ questionId: "qst_env", selected: [], otherText: "Canary only", text: "", confirmed: null });
  expect(answerBody(value, answers, "key").answers[2]).toEqual({ questionId: "qst_window", skip: true });
});
