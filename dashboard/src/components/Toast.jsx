import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Button } from "./ui.jsx";

/** The topmost open modal dialog, if any. Content outside it is inert while it is open. */
function topDialog() {
  const open = [...document.querySelectorAll("dialog[open]")].filter(dialog => dialog.matches(":modal"));
  return open.at(-1) || null;
}

/**
 * Application notice. While a modal dialog is open it renders inside that dialog, because everything
 * outside a modal dialog is inert and drawn beneath it; otherwise it renders at the page level.
 */
export default function Toast({ message, onDismiss }) {
  const [host, setHost] = useState(() => topDialog());
  useEffect(() => {
    const update = () => setHost(topDialog());
    update();
    const observer = new MutationObserver(update);
    observer.observe(document.body, { subtree: true, attributes: true, attributeFilter: ["open"], childList: true });
    return () => observer.disconnect();
  }, []);
  const toast = <div className="toast" role="alert">
    <span>{message}</span>
    <Button label="common.dismiss" help="common.dismiss" icon="close" iconOnly onClick={onDismiss} />
  </div>;
  return host ? createPortal(toast, host) : toast;
}
