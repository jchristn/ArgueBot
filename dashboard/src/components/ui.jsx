import { useEffect, useId, useRef } from "react";
import { useTranslation } from "react-i18next";
export function Icon({ name }) {
  const paths = {
    plus: "M12 4v16M4 12h16",
    download: "M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4",
    moon: "M20 14A8 8 0 0 1 10 4a8 8 0 1 0 10 10",
    sun: "M12 2v2m0 16v2M2 12h2m16 0h2M5 5l2 2m10 10 2 2M5 19l2-2M17 7l2-2M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
    close: "m6 6 12 12M6 18 18 6",
    menu: "M4 6h16M4 12h16M4 18h16",
    arrow: "m9 5 7 7-7 7",
    chat: "M4 4h16v12H8l-4 4V4",
    grid: "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z",
    settings:
      "M12 3v3m0 12v3M3 12h3m12 0h3M6 6l2 2m8 8 2 2M6 18l2-2m8-8 2-2M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0",
    logout: "M9 4H4v16h5M10 12h11m-4-4 4 4-4 4",
    external: "M14 3h7v7m0-7L10 14M10 4H4v16h16v-6",
    refresh: "M20 7v5h-5M4 17v-5h5M6 6a8 8 0 0 1 14 6M18 18A8 8 0 0 1 4 12",
    users:
      "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M12 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M17 4a4 4 0 0 1 0 8m5 9v-2a4 4 0 0 0-3-4",
    search: "M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
  };
  return (
    <svg
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name] || paths.chat} />
    </svg>
  );
}
export function Button({
  label,
  help,
  icon,
  iconOnly = false,
  children,
  className = "",
  ...props
}) {
  const { t } = useTranslation();
  const text = label ? t(label) : undefined;
  return (
    <button
      {...props}
      className={`button ${iconOnly ? "icon-button" : ""} ${className}`}
      title={t(help || `${label}.help`, { defaultValue: text })}
      aria-label={text}
    >
      {icon && <Icon name={icon} />} {!iconOnly && (children || text)}
    </button>
  );
}
export function Field({ label, children }) {
  const { t } = useTranslation();
  const id = useId();
  return (
    <label className="field" title={t(`${label}.help`)}>
      <span>{t(label)}</span>
      {typeof children === "function"
        ? children({
            id,
            title: t(`${label}.help`),
            "aria-label": t(label),
            "aria-describedby": id + "-hint",
          })
        : children}
      <small id={id + "-hint"}>{t(`${label}.help`)}</small>
    </label>
  );
}
export function Modal({ title, onClose, children, wide = false }) {
  const { t } = useTranslation();
  const ref = useRef();
  const titleId = useId();
  useEffect(() => {
    const dialog = ref.current;
    dialog.showModal();
    return () => dialog.close();
  }, []);
  return (
    <dialog
      ref={ref}
      className={wide ? "wide" : ""}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      aria-labelledby={titleId}
    >
      <header>
        <h2 id={titleId}>{t(title)}</h2>
        <Button
          label="common.close"
          help="common.close"
          icon="close"
          iconOnly
          onClick={onClose}
        />
      </header>
      <div className="modal-body">{children}</div>
    </dialog>
  );
}
export function ErrorPanel({ error, retry }) {
  const { t } = useTranslation();
  return (
    <div className="error-panel" role="alert">
      <strong>{t("common.error")}</strong>
      <p>{error.message}</p>
      {error.requestId && <code>{error.requestId}</code>}
      {retry && (
        <Button label="common.retry" help="common.retry" onClick={retry} />
      )}
    </div>
  );
}
export function Pagination({ page, onChange }) {
  const { t } = useTranslation();
  const pages = Math.max(1, Math.ceil(page.totalCount / page.pageSize));
  return (
    <div className="pagination">
      <span>{t("common.total", { count: page.totalCount })}</span>
      <div>
        <Button
          label="common.first"
          help="common.first"
          disabled={page.pageNumber <= 1}
          onClick={() => onChange({ pageNumber: 1 })}
        >
          «
        </Button>
        <Button
          label="common.previous"
          help="common.previous"
          disabled={page.pageNumber <= 1}
          onClick={() => onChange({ pageNumber: page.pageNumber - 1 })}
        >
          ‹
        </Button>
        <label title={t("common.jump.help")}>
          {t("common.page", { page: page.pageNumber, pages })}
          <input
            type="number"
            min="1"
            max={pages}
            value={page.pageNumber}
            aria-label={t("common.jump")}
            title={t("common.jump.help")}
            onChange={(event) => {
              const value = Number(event.target.value);
              if (value >= 1 && value <= pages) onChange({ pageNumber: value });
            }}
          />
        </label>
        <Button
          label="common.next"
          help="common.next"
          disabled={page.pageNumber >= pages}
          onClick={() => onChange({ pageNumber: page.pageNumber + 1 })}
        >
          ›
        </Button>
        <Button
          label="common.last"
          help="common.last"
          disabled={page.pageNumber >= pages}
          onClick={() => onChange({ pageNumber: pages })}
        >
          »
        </Button>
        <select
          title={t("common.pageSize.help")}
          aria-label={t("common.pageSize")}
          value={page.pageSize}
          onChange={(event) =>
            onChange({ pageSize: Number(event.target.value), pageNumber: 1 })
          }
        >
          {[10, 25, 50, 100].map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
