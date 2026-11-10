import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Field, Modal } from "./ui.jsx";

const deletable = () => "";

/**
 * Row selection for a table whose rows can be deleted. `blocked(item)` returns "" for a row that can be deleted, or the
 * translation key that explains why it cannot; such rows show a disabled checkbox. Selections drop rows that leave the page,
 * and Shift+click selects the range from the last row clicked.
 */
export function useSelection(items, blocked = deletable) {
  const [selected, setSelected] = useState(() => new Set());
  const anchor = useRef(null);
  const rows = useMemo(() => items || [], [items]);
  const reasons = useMemo(() => new Map(rows.map(item => [item.id, blocked(item) || ""])), [rows, blocked]);
  const eligible = useMemo(() => rows.filter(item => !reasons.get(item.id)).map(item => item.id), [rows, reasons]);
  const key = eligible.join(",");
  useEffect(() => {
    setSelected(current => {
      const kept = new Set([...current].filter(id => eligible.includes(id)));
      return kept.size === current.size ? current : kept;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  const toggle = (id, range = false) => setSelected(current => {
    const next = new Set(current);
    const order = rows.map(item => item.id);
    const from = order.indexOf(anchor.current);
    const to = order.indexOf(id);
    if (range && from >= 0 && to >= 0) {
      const select = !current.has(id);
      for (const target of order.slice(Math.min(from, to), Math.max(from, to) + 1)) if (eligible.includes(target)) { if (select) next.add(target); else next.delete(target); }
    } else if (next.has(id)) next.delete(id);
    else next.add(id);
    anchor.current = id;
    return next;
  });
  const allSelected = eligible.length > 0 && eligible.every(id => selected.has(id));
  return {
    count: selected.size,
    available: eligible.length,
    allSelected,
    someSelected: selected.size > 0 && !allSelected,
    reason: id => reasons.get(id) || "",
    has: id => selected.has(id),
    toggle,
    toggleAll: () => setSelected(allSelected ? new Set() : new Set(eligible)),
    keep: ids => setSelected(new Set(ids)),
    clear: () => setSelected(new Set()),
    items: () => rows.filter(item => selected.has(item.id)),
  };
}

/** The header cell that selects or clears every row on the page that can be deleted. */
export function SelectHeader({ selection }) {
  const { t } = useTranslation();
  const label = t(selection.available ? "bulk.selectAll" : "bulk.selectAll.none");
  return <th scope="col" className="select-cell">
    <input type="checkbox" aria-label={label} title={label} checked={selection.allSelected} disabled={!selection.available}
      ref={element => { if (element) element.indeterminate = selection.someSelected; }} onChange={selection.toggleAll} />
  </th>;
}

/** The cell that selects one row. Clicking anywhere in the cell toggles it; a row that cannot be deleted shows a disabled box that says why. */
export function SelectCell({ selection, item, name }) {
  const { t } = useTranslation();
  const reason = selection.reason(item.id);
  const label = reason ? t(reason) : t("bulk.select", { name });
  return <td className="select-cell" title={label} onClick={event => {
    event.stopPropagation();
    if (!reason && event.target === event.currentTarget) selection.toggle(item.id, event.shiftKey);
  }}>
    <input type="checkbox" aria-label={reason ? t("bulk.select", { name }) + ". " + label : label} disabled={Boolean(reason)} checked={selection.has(item.id)}
      onClick={event => event.stopPropagation()} onChange={event => selection.toggle(item.id, Boolean(event.nativeEvent?.shiftKey))} />
  </td>;
}

/** The toolbar button that appears once one or more rows are selected. */
export function BulkDeleteButton({ selection, onClick }) {
  const { t } = useTranslation();
  if (selection.count === 0) return null;
  return <Button help="bulk.deleteMultiple.help" className="danger bulk-delete" onClick={onClick}>
    {t("bulk.deleteMultiple.count", { count: selection.count })}
  </Button>;
}

/** Confirm, then delete the selected records one at a time. Records that fail stay selected and are listed with the reason. */
export function BulkDeleteDialog({ items, nameOf, remove, describe, confirmWord = false, onClose, onDone }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState(0);
  const [failures, setFailures] = useState([]);
  const [typed, setTyped] = useState("");
  const word = t("bulk.confirmWord");
  const shown = items.slice(0, 10);
  const run = async () => {
    setBusy(true); setFailures([]); setProgress(0);
    const failed = [];
    for (let index = 0; index < items.length; index++) {
      try { await remove(items[index]); }
      catch (failure) { failed.push({ item: items[index], message: (describe && describe(failure, items[index])) || failure.message || String(failure) }); }
      setProgress(index + 1);
    }
    setBusy(false);
    if (failed.length === 0) { onDone([]); return; }
    setFailures(failed);
  };
  const finished = failures.length > 0;
  const close = () => { if (busy) return; if (finished) onDone(failures.map(entry => entry.item.id)); else onClose(); };
  return <Modal title={items.length === 1 ? "common.delete" : "bulk.title"} onClose={close}>
    {!finished && <>
      <p>{t("bulk.confirm", { count: items.length })}</p>
      <ul className="bulk-list">{shown.map(item => <li key={item.id}>{nameOf(item)}</li>)}</ul>
      {items.length > shown.length && <p className="muted">{t("bulk.more", { count: items.length - shown.length })}</p>}
      <p className="muted">{t("bulk.irreversible")}</p>
      {confirmWord && <Field label="bulk.type">{props => <input {...props} data-autofocus autoComplete="off" placeholder={word} value={typed} onChange={event => setTyped(event.target.value)} />}</Field>}
    </>}
    {busy && <p className="muted" role="status">{t("bulk.progress", { done: progress, count: items.length })}</p>}
    {finished && <div className="notice warning" role="alert">
      <p>{t("bulk.partial", { deleted: items.length - failures.length, failed: failures.length })}</p>
      <ul className="bulk-list">{failures.map(entry => <li key={entry.item.id}><strong>{nameOf(entry.item)}</strong>: {entry.message}</li>)}</ul>
    </div>}
    <footer className="dialog-actions">
      {finished
        ? <Button label="common.close" onClick={close} />
        : <>
          <Button label="common.cancel" disabled={busy} onClick={onClose} />
          <Button help="bulk.deleteMultiple.help" className="danger" disabled={busy || (confirmWord && typed.trim().toLowerCase() !== word.toLowerCase())} onClick={run}>{t("bulk.deleteCount", { count: items.length })}</Button>
        </>}
    </footer>
  </Modal>;
}
