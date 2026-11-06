import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import i18n from "../i18n/index.js";
import Resources from "./Resources.jsx";
afterEach(cleanup);
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const summary = (name, extra = {}) => ({ id: "res_" + name, name, title: name.toUpperCase(), description: "About " + name, tags: [], bytes: 2048, locked: false, revision: 1, updatedByKind: "user", updatedByName: "Admin", updatedUtc: "2026-11-05T10:00:00Z", ...extra });

function renderList(api) {
  return render(<MemoryRouter initialEntries={["/dashboard/resources"]}><Routes><Route element={<OutletHost api={api} />}><Route path="/dashboard/resources" element={<Resources />} /></Route></Routes></MemoryRouter>);
}
function OutletHost({ api }) {
  return <Outlet context={{ api, notify: vi.fn() }} />;
}

it("lists resources with agent authorship, locks and sizes, and filters by search and tag", async () => {
  const api = { resources: vi.fn().mockResolvedValue({ items: [summary("billing", { tags: ["finance"], locked: true }), summary("debate-notes", { updatedByKind: "agent", updatedByName: "Claude" })], totalCount: 2 }) };
  renderList(api);
  expect(await screen.findByText("billing")).toBeInTheDocument();
  expect(screen.getByText(i18n.t("resources.lockedBadge"))).toBeInTheDocument();
  expect(screen.getByText(i18n.t("resources.agentNamed", { name: "Claude" }))).toBeInTheDocument();
  expect(screen.getAllByText("2 KiB")).toHaveLength(2);
  fireEvent.change(screen.getByLabelText(i18n.t("resources.tagFilter")), { target: { value: "finance" } });
  expect(screen.queryByText("debate-notes")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText(i18n.t("resources.tagFilter")), { target: { value: "" } });
  fireEvent.change(screen.getByLabelText(i18n.t("resources.search")), { target: { value: "notes" } });
  expect(screen.queryByText("billing")).not.toBeInTheDocument();
  expect(screen.getByText("debate-notes")).toBeInTheDocument();
});

it("shows an empty state that points to adding or importing", async () => {
  renderList({ resources: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }) });
  expect(await screen.findByText(i18n.t("resources.empty"))).toBeInTheDocument();
});
