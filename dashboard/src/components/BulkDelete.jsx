import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Field, Modal } from "./ui.jsx";

/** Row selection for a table whose rows can be deleted. Rows that cannot be deleted are never selectable, and selections drop rows that leave the page. */
export function useSelection(items, selectable = () => true) {
  const [selected, setSelected] = useState(() => new Set());
  const eligible = useMemo(() => (items || []).filter(selectable).map(item => item.id), [items, selectable]);
  const key = eligible.join(",");
  useEffect(() => {
    setSelected(current => {
      const kept = new Set([...current].filter(id => eligible.includes(id)));
      return kept.size === current.size ? current : kept;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  const toggle = id => setSelected(current => { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const allSelected = eligible.length > 0 && eligible.every(id => selected.has(id));
  return {
    selected,
    count: selected.size,
    allSelected,
    someSelected: selected.size > 0 && !allSelected,
    eligible: id => eligible.includes(id),
    has: id => selected.has(id),
    toggle,
    toggleAll: () => setSelected(allSelected ? new Set() : new Set(eligible)),
    keep: ids => setSelected(new Set(ids)),
    clear: () => setSelected(new Set()),
    items: () => (items || []).filter(item => selected.has(item.id)),
  };
}

/** The header cell that selects or clears every selectable row on the page. */
export function SelectHeader({ selection }) {
  const { t } = useTranslation();
  return <th scope="col" className="select-cell">
    <input type="checkbox" aria-label={t("bulk.selectAll")} title={t("bulk.selectAll")} checked={selection.allSelected}
      ref={element => { if (element) element.indeterminate = selection.someSelected; }} onChange={selection.toggleAll} />
  </th>;
}

/** The cell that selects one row. It stays empty for a row that cannot be deleted. */
export function SelectCell({ selection, item, name }) {
  const { t } = useTranslation();
  return <td className="select-cell">
    {selection.eligible(item.id) && <input type="checkbox" aria-label={t("bulk.select", { name })} title={t("bulk.select", { name })} checked={selection.has(item.id)} onChange={() => selection.toggle(item.id)} />}
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
  return <Modal title="bulk.title" onClose={close}>
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
