import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "../i18n/index.js";
import ResourceEditor, { resourceDraft, resourceRequest } from "./ResourceEditor.jsx";
import ResourceHistory from "./ResourceHistory.jsx";
afterEach(cleanup);
// jsdom does not implement modal dialogs; open and close them as attributes so roles and focus queries work.
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const current = { id: "res_1", name: "billing", title: "Billing", description: "How invoices work.", tags: ["finance"], body: "Invoices go out on the 1st.", locked: false, revision: 3, updatedByKind: "agent", updatedByName: "Claude", updatedUtc: "2026-11-05T10:00:00Z" };

it("turns form text into a save request", () => {
  expect(resourceRequest({ ...resourceDraft(current), tags: "Finance, ops, finance", name: " Billing " })).toEqual({ id: "res_1", name: "Billing", title: "Billing", description: "How invoices work.", tags: ["finance", "ops"], body: "Invoices go out on the 1st.", locked: false, revision: 3 });
});

it("marks agent authorship and explains a revision conflict while keeping the draft", async () => {
  const conflict = Object.assign(new Error("This resource changed since you read it."), { code: "revision_conflict", status: 409 });
  const latest = { ...current, body: "Invoices go out on the 2nd.", revision: 4, updatedByKind: "user", updatedByName: "Admin" };
  const api = { saveResource: vi.fn().mockRejectedValue(conflict), resource: vi.fn().mockResolvedValue(latest) };
  const onSaved = vi.fn();
  render(<ResourceEditor api={api} resource={current} onSaved={onSaved} onClose={() => {}} />);
  const dialog = screen.getByRole("dialog");
  expect(within(dialog).getByText(i18n.t("resources.agent"))).toBeInTheDocument();
  fireEvent.change(within(dialog).getByLabelText(i18n.t("resources.body")), { target: { value: "My edit." } });
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("common.save") }));
  expect(await within(dialog).findByText(i18n.t("resources.conflict"))).toBeInTheDocument();
  expect(api.saveResource).toHaveBeenCalledWith(expect.objectContaining({ id: "res_1", revision: 3, body: "My edit." }));
  expect(within(dialog).getByText("My edit.", { selector: "pre" })).toBeInTheDocument();
  expect(onSaved).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("resources.reloadLatest") }));
  await waitFor(() => expect(within(dialog).getByLabelText(i18n.t("resources.body"))).toHaveValue("Invoices go out on the 2nd."));
  expect(within(dialog).queryByText(i18n.t("resources.conflict"))).not.toBeInTheDocument();
});

it("refuses invalid names and oversized bodies before saving", () => {
  render(<ResourceEditor api={{}} resource={null} onSaved={() => {}} onClose={() => {}} />);
  const dialog = screen.getByRole("dialog");
  const save = within(dialog).getByRole("button", { name: i18n.t("common.save") });
  fireEvent.change(within(dialog).getByLabelText(i18n.t("resources.name")), { target: { value: "bad name" } });
  expect(save).toBeDisabled();
  expect(within(dialog).getByText(i18n.t("resources.nameInvalid"))).toBeInTheDocument();
  fireEvent.change(within(dialog).getByLabelText(i18n.t("resources.name")), { target: { value: "team/notes" } });
  expect(save).toBeEnabled();
  fireEvent.change(within(dialog).getByLabelText(i18n.t("resources.body")), { target: { value: "x".repeat(262145) } });
  expect(save).toBeDisabled();
});

it("lists history with authors, diffs a revision against the current text and reverts it", async () => {
  const revisions = [
    { revision: 3, change: "append", authorKind: "agent", authorName: "Claude", createdUtc: "2026-11-05T10:00:00Z" },
    { revision: 2, change: "update", authorKind: "user", authorName: "Admin", createdUtc: "2026-11-05T09:00:00Z" },
  ];
  const reverted = { ...current, revision: 4, body: "Invoices go out on the 1st.\nOld line." };
  const api = {
    resourceRevisions: vi.fn().mockResolvedValue(revisions),
    resourceRevision: vi.fn().mockResolvedValue({ ...revisions[1], body: "Invoices go out on the 1st.\nOld line.", runId: "" }),
    revertResource: vi.fn().mockResolvedValue(reverted),
  };
  const onReverted = vi.fn().mockResolvedValue();
  render(<ResourceHistory api={api} resource={current} onReverted={onReverted} />);
  expect(await screen.findByText(i18n.t("resources.agentNamed", { name: "Claude" }))).toBeInTheDocument();
  await screen.findByText(i18n.t("resources.diffTitle", { revision: 2, current: 3 }));
  const diff = screen.getByLabelText(i18n.t("resources.diff"));
  expect(diff.querySelector(".diff-removed")).toHaveTextContent("Old line.");
  fireEvent.click(screen.getByRole("button", { name: i18n.t("resources.revert") }));
  await waitFor(() => expect(onReverted).toHaveBeenCalledWith(reverted));
  expect(api.revertResource).toHaveBeenCalledWith("res_1", 2, 3);
});
