import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useTranslation } from "react-i18next";
import { Icon } from "./ui.jsx";

const margin = 8;

/** Place a menu next to its trigger: below when it fits, otherwise above, always clamped inside the viewport. */
export function placeMenu(trigger, menu, viewport) {
  const width = Math.min(menu.width, viewport.width - margin * 2);
  const height = Math.min(menu.height, viewport.height - margin * 2);
  const below = viewport.height - trigger.bottom - margin;
  const above = trigger.top - margin;
  let top = below >= height || below >= above ? trigger.bottom + 4 : trigger.top - height - 4;
  top = Math.max(margin, Math.min(top, viewport.height - height - margin));
  let left = viewport.rtl ? trigger.left : trigger.right - width;
  left = Math.max(margin, Math.min(left, viewport.width - width - margin));
  return { top, left, maxHeight: viewport.height - margin * 2 };
}

/**
 * Row actions behind a single "more" button. The menu renders in a body-level layer above tables and
 * dialogs and stays inside the viewport. Items: { label, help, onClick, danger, disabled, hidden }.
 */
export default function ActionMenu({ items, label = "common.actions", context, children, triggerClassName = "" }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState(null);
  const trigger = useRef();
  const menu = useRef();
  const id = useId();
  const visible = items.filter(item => item && !item.hidden);
  const close = useCallback((restore = true) => { setOpen(false); setPosition(null); if (restore) trigger.current?.focus(); }, []);
  const place = useCallback(() => {
    if (!trigger.current || !menu.current) return;
    const box = menu.current.getBoundingClientRect();
    setPosition(placeMenu(trigger.current.getBoundingClientRect(), { width: box.width, height: menu.current.scrollHeight }, { width: window.innerWidth, height: window.innerHeight, rtl: document.documentElement.dir === "rtl" }));
  }, []);
  useLayoutEffect(() => { if (open) place(); }, [open, place]);
  useEffect(() => {
    if (!open) return undefined;
    const outside = event => { if (!menu.current?.contains(event.target) && !trigger.current?.contains(event.target)) close(false); };
    const moved = event => { if (!menu.current?.contains(event.target)) place(); };
    document.addEventListener("pointerdown", outside, true);
    window.addEventListener("resize", place);
    window.addEventListener("scroll", moved, true);
    return () => { document.removeEventListener("pointerdown", outside, true); window.removeEventListener("resize", place); window.removeEventListener("scroll", moved, true); };
  }, [open, close, place]);
  useEffect(() => { if (open && position) menu.current?.querySelector("[role=menuitem]:not(:disabled)")?.focus(); }, [open, position]);
  const onKeyDown = event => {
    const entries = [...menu.current.querySelectorAll("[role=menuitem]:not(:disabled)")];
    const index = entries.indexOf(document.activeElement);
    const moves = { ArrowDown: index + 1, ArrowUp: index - 1, Home: 0, End: entries.length - 1 };
    if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); }
    else if (event.key === "Tab") close(false);
    else if (event.key in moves && entries.length) { event.preventDefault(); entries[(moves[event.key] + entries.length) % entries.length].focus(); }
  };
  const name = context ? t("actions.for", { name: context }) : t(label);
  // The dialog layer keeps the menu above an open <dialog>; otherwise the menu goes to the body.
  const host = trigger.current?.closest("dialog") || document.body;
  return <>
    <button ref={trigger} type="button" className={children ? "button action-menu-trigger " + triggerClassName : "button icon-button action-menu-trigger"} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? id : undefined} aria-label={name} title={name} onClick={event => { event.stopPropagation(); if (open) close(false); else setOpen(true); }}>
      {children || <Icon name="more" />}
    </button>
    {open && createPortal(<div ref={menu} id={id} role="menu" aria-label={name} className="action-menu" style={position ? { top: position.top, left: position.left, maxHeight: position.maxHeight } : { visibility: "hidden", top: 0, left: 0 }} onKeyDown={onKeyDown} onClick={event => event.stopPropagation()}>
      {visible.map(item => <button key={item.label} type="button" role="menuitem" className={item.danger ? "danger" : ""} disabled={item.disabled} title={item.help ? t(item.help) : undefined} onClick={() => { close(); item.onClick(); }}>{t(item.label)}</button>)}
    </div>, host)}
  </>;
}
