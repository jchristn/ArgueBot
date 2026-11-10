import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "../i18n/index.js";
import { BulkDeleteButton, BulkDeleteDialog, SelectCell, SelectHeader, useSelection } from "./BulkDelete.jsx";
afterEach(cleanup);
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const rows = [{ id: "a", name: "Alpha" }, { id: "b", name: "Bravo" }, { id: "c", name: "Charlie", locked: true }];
const unlocked = row => !row.locked;

function Table() {
  const selection = useSelection(rows, unlocked);
  return <>
    <BulkDeleteButton selection={selection} onClick={() => {}} />
    <table><thead><tr><SelectHeader selection={selection} /><th>Name</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.id}><SelectCell selection={selection} item={row} name={row.name} /><td>{row.name}</td></tr>)}</tbody></table>
    <output data-testid="count">{selection.count}</output>
  </>;
}

it("selects deletable rows only and shows Delete multiple once one is selected", () => {
  render(<Table />);
  expect(screen.queryByRole("button", { name: /Delete multiple/ })).toBeNull();
  expect(screen.queryByRole("checkbox", { name: i18n.t("bulk.select", { name: "Charlie" }) })).toBeNull();
  fireEvent.click(screen.getByRole("checkbox", { name: i18n.t("bulk.select", { name: "Alpha" }) }));
  expect(screen.getByRole("button", { name: /Delete multiple/ })).toHaveTextContent("Delete multiple (1)");
  fireEvent.click(screen.getByRole("checkbox", { name: i18n.t("bulk.selectAll") }));
  expect(screen.getByTestId("count")).toHaveTextContent("2");
  fireEvent.click(screen.getByRole("checkbox", { name: i18n.t("bulk.selectAll") }));
  expect(screen.getByTestId("count")).toHaveTextContent("0");
});

it("deletes each selected record and keeps the ones that failed selected with the reason", async () => {
  const remove = vi.fn(async row => { if (row.id === "b") throw new Error("Bravo is in use"); });
  const onDone = vi.fn();
  render(<BulkDeleteDialog items={rows.slice(0, 2)} nameOf={row => row.name} remove={remove} onClose={vi.fn()} onDone={onDone} />);
  const dialog = await screen.findByRole("dialog");
  expect(dialog).toHaveTextContent("Alpha");
  expect(dialog).toHaveTextContent("Bravo");
  fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("bulk.deleteCount", { count: 2 }) }));
  await waitFor(() => expect(within(dialog).getByRole("alert")).toHaveTextContent("Bravo is in use"));
  expect(remove).toHaveBeenCalledTimes(2);
  expect(onDone).not.toHaveBeenCalled();
  fireEvent.click(within(dialog.querySelector(".dialog-actions")).getByRole("button", { name: i18n.t("common.close") }));
  expect(onDone).toHaveBeenCalledWith(["b"]);
});

it("requires the confirmation word before deleting accounts", async () => {
  const remove = vi.fn().mockResolvedValue(undefined);
  const onDone = vi.fn();
  render(<BulkDeleteDialog items={rows.slice(0, 1)} nameOf={row => row.name} remove={remove} confirmWord onClose={vi.fn()} onDone={onDone} />);
  const dialog = await screen.findByRole("dialog");
  const button = within(dialog).getByRole("button", { name: i18n.t("bulk.deleteCount", { count: 1 }) });
  expect(button).toBeDisabled();
  fireEvent.change(within(dialog).getByRole("textbox"), { target: { value: "Delete" } });
  expect(button).toBeEnabled();
  fireEvent.click(button);
  await waitFor(() => expect(onDone).toHaveBeenCalledWith([]));
  expect(remove).toHaveBeenCalledWith(rows[0]);
});
