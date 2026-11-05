import { useTranslation } from "react-i18next";
import CopyableId from "./CopyableId.jsx";
import { Button, Modal } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

/** Readable value for one field of a record: dates for *Utc fields, yes/no for flags, copyable IDs, lists joined. */
export function detailValue(key, value, t) {
  if (typeof value === "boolean") return t(value ? "common.yes" : "common.no");
  if (/Utc$/.test(key) && typeof value === "string") return <time dateTime={value}>{formatDate(value)}</time>;
  if (Array.isArray(value)) return value.map(String).join(", ");
  if (typeof value === "object") return <code>{JSON.stringify(value)}</code>;
  if (key === "id" || /Id$/.test(key)) return <CopyableId value={String(value)} />;
  return String(value);
}

/** The fields of a record worth showing: skip empty values and anything listed in omit. */
export function detailEntries(record, omit = []) {
  return Object.entries(record || {}).filter(([key, value]) => !omit.includes(key) && value !== null && value !== undefined && value !== "" && !(Array.isArray(value) && value.length === 0));
}

/** A read-only view of one record, for tables whose rows have nothing to edit. Labels come from {prefix}.field.{name} when translated. */
export default function DetailsDialog({ title, heading, record, prefix, omit = [], actions = null, onClose }) {
  const { t, i18n } = useTranslation();
  const label = key => i18n.exists(`${prefix}.field.${key}`) ? t(`${prefix}.field.${key}`) : key;
  return <Modal title={title} wide onClose={onClose}>
    {heading && <h3>{heading}</h3>}
    <dl className="server-details record-details">
      {detailEntries(record, omit).map(([key, value]) => <div key={key} className="detail-row"><dt>{label(key)}</dt><dd>{detailValue(key, value, t)}</dd></div>)}
    </dl>
    <footer className="dialog-actions">{actions}<Button label="common.close" className="primary" onClick={onClose} /></footer>
  </Modal>;
}
