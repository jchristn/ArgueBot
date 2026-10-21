import { useTranslation } from "react-i18next";
export default function ObjectRow({ onOpen, children, inspect = false, ...props }) {
  const { t } = useTranslation();
  const interactive = event => event.target.closest("button,a,input,select,textarea,summary,.copyable-id");
  return <tr {...props} className="object-row" tabIndex={0} title={t(inspect ? "rows.inspect" : "rows.edit")}
    onClick={event => { if (!interactive(event) && !window.getSelection()?.toString()) onOpen(); }}
    onKeyDown={event => { if (event.target === event.currentTarget && ["Enter", " "].includes(event.key)) { event.preventDefault(); onOpen(); } }}>
    {children}
  </tr>;
}
