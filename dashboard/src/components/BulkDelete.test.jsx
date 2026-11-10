import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import i18n from "../i18n/index.js";
import { BulkDeleteButton, BulkDeleteDialog, SelectCell, SelectHeader, useSelection } from "./BulkDelete.jsx";
afterEach(cleanup);
HTMLDialogElement.prototype.showModal ||= function showModal() { this.setAttribute("open", ""); };
HTMLDialogElement.prototype.close ||= function close() { this.removeAttribute("open"); };

const rows = [{ id: "a", name: "Alpha" }, { id: "b", name: "Bravo" }, { id: "c", name: "Charlie", locked: true }, { id: "d", name: "Delta" }, { id: "e", name: "Echo" }];
const locked = row => row.locked ? "bulk.blocked.protected" : "";

function Table({ items = rows }) {
  const selection = useSelection(items, locked);
  return <>
    <BulkDeleteButton selection={selection} onClick={() => {}} />
    <table><thead><tr><SelectHeader selection={selection} /><th>Name</th></tr></thead>
      <tbody>{items.map(row => <tr key={row.id}><SelectCell selection={selection} item={row} name={row.name} /><td>{row.name}</td></tr>)}</tbody></table>
    <output data-testid="count">{selection.count}</output>
  </>;
}
const box = name => screen.getByRole("checkbox", { name: new RegExp("^" + i18n.t("bulk.select", { name })) });

it("gives every row a checkbox, disables the ones that cannot be deleted with the reason, and shows Delete multiple once one is selected", () => {
  render(<Table />);
  expect(screen.getAllByRole("checkbox")).toHaveLength(rows.length + 1);
  expect(screen.queryByRole("button", { name: /Delete multiple/ })).toBeNull();
  expect(box("Charlie")).toBeDisabled();
  expect(box("Charlie").closest("td")).toHaveAttribute("title", i18n.t("bulk.blocked.protected"));
  fireEvent.click(box("Alpha"));
  expect(screen.getByRole("button", { name: /Delete multiple/ })).toHaveTextContent("Delete multiple (1)");
  fireEvent.click(screen.getByRole("checkbox", { name: i18n.t("bulk.selectAll") }));
  expect(screen.getByTestId("count")).toHaveTextContent("4");
  expect(box("Charlie")).not.toBeChecked();
  fireEvent.click(screen.getByRole("checkbox", { name: i18n.t("bulk.selectAll") }));
  expect(screen.getByTestId("count")).toHaveTextContent("0");
});

it("toggles a row from anywhere in its checkbox cell and selects a range with Shift, skipping rows that cannot be deleted", () => {
  render(<Table />);
  fireEvent.click(box("Bravo").closest("td"));
  expect(box("Bravo")).toBeChecked();
  fireEvent.click(box("Bravo").closest("td"));
  expect(box("Bravo")).not.toBeChecked();
  fireEvent.click(box("Alpha"));
  fireEvent.click(box("Echo"), { shiftKey: true });
  expect(screen.getByTestId("count")).toHaveTextContent("4");
  expect(box("Charlie")).not.toBeChecked();
  expect(box("Delta")).toBeChecked();
});

it("disables select-all and says why when nothing on the page can be deleted", () => {
  render(<Table items={[rows[2]]} />);
  const all = screen.getByRole("checkbox", { name: i18n.t("bulk.selectAll.none") });
  expect(all).toBeDisabled();
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
