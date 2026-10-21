import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { copyText } from "../utils/clipboard.js";
import { Button, Modal } from "./ui.jsx";

export default function CopyButton({ value, loadText, label = "common.copy", help, iconOnly = false, disabled = false }) {
  const { t } = useTranslation();
  const [state, setState] = useState("idle");
  const [manual, setManual] = useState(null);
  const [error, setError] = useState("");
  const field = useRef();
  useEffect(() => { if (state === "copied") { const timer = setTimeout(() => setState("idle"), 2000); return () => clearTimeout(timer); } }, [state]);
  useEffect(() => { if (manual !== null) { field.current?.focus(); field.current?.select(); } }, [manual]);
  const copy = async () => {
    if (state === "busy") return;
    setState("busy"); setError("");
    try {
      const text = loadText ? await loadText() : value;
      if (typeof text !== "string") throw new Error(t("copy.unavailable"));
      if (await copyText(text)) setState("copied");
      else { setManual(text); setState("idle"); }
    } catch (failure) { setError(failure.message); setState("idle"); }
  };
  return <span className="copy-control">
    <Button label={state === "copied" ? "common.copied" : label} help={help || `${label}.help`} icon={state === "copied" ? "check" : "copy"} iconOnly={iconOnly} type="button" disabled={disabled || state === "busy"} onClick={copy} />
    <span className="sr-only" role="status">{state === "copied" ? t("common.copied") : state === "busy" ? t("common.loading") : ""}</span>
    {error && <span role="alert">{error}</span>}
    {manual !== null && <Modal title="copy.manual" onClose={() => setManual(null)} wide>
      <p>{t("copy.manual.help")}</p>
      <textarea ref={field} className="copy-manual" aria-label={t("copy.manualText")} readOnly value={manual} onFocus={event => event.target.select()} />
      <footer><Button label="common.close" onClick={() => setManual(null)} /></footer>
    </Modal>}
  </span>;
}
