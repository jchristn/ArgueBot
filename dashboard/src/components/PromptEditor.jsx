import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";

const purposes = ["question", "response", "synthesis", "ratification", "compaction"];

/** Placeholder problems the service would reject, so the editor can show them while typing. */
export function promptProblems(body, purpose, parameters) {
  const used = new Set();
  const problems = [];
  for (const match of body.matchAll(/\{\{|\}\}|\{([a-z_]+)\}/g)) {
    if (!match[1]) continue;
    const parameter = parameters.find(item => item.name === match[1]);
    if (!parameter) problems.push({ kind: "unknown", name: match[1] });
    else if (!parameter.purposes.includes(purpose)) problems.push({ kind: "unavailable", name: match[1] });
    used.add(match[1]);
  }
  for (const parameter of parameters) if (parameter.requiredFor.includes(purpose) && !used.has(parameter.name)) problems.push({ kind: "missing", name: parameter.name });
  return problems.filter((problem, index) => problems.findIndex(other => other.kind === problem.kind && other.name === problem.name) === index);
}

/** View a factory prompt, or create and edit a workspace prompt, beside a legend of its parameters. */
export default function PromptEditor({ api, prompt, parameters, onSaved, onClose, onDuplicate }) {
  const { t } = useTranslation();
  const readOnly = Boolean(prompt.factory);
  const [value, setValue] = useState(() => ({ ...prompt }));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const body = useRef();
  const available = parameters.filter(item => item.purposes.includes(value.purpose));
  const problems = useMemo(() => promptProblems(value.body, value.purpose, parameters), [value.body, value.purpose, parameters]);
  const change = (key, next) => { setValue(current => ({ ...current, [key]: next })); setError(null); };
  const insert = name => {
    const element = body.current;
    const token = "{" + name + "}";
    const start = element?.selectionStart ?? value.body.length;
    const end = element?.selectionEnd ?? value.body.length;
    change("body", value.body.slice(0, start) + token + value.body.slice(end));
    requestAnimationFrame(() => { element?.focus(); element?.setSelectionRange(start + token.length, start + token.length); });
  };
  const save = async event => {
    event.preventDefault();
    setBusy(true); setError(null);
    try { await api.savePrompt(value); await onSaved(); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  };
  const title = readOnly ? "prompts.viewTitle" : prompt.id ? "prompts.editTitle" : "prompts.addTitle";
  return <Modal title={title} wide className="prompt-editor" onClose={() => !busy && onClose()}>
    <form onSubmit={save}>
      {readOnly && <p className="notice">{t("prompts.factoryNotice")}</p>}
      <div className="form-grid">
        <Field label="prompts.name">{props => <input {...props} data-autofocus={readOnly ? undefined : true} required maxLength={200} readOnly={readOnly} value={value.name} onChange={event => change("name", event.target.value)} />}</Field>
        <Field label="prompts.purposeField">{props => <select {...props} value={value.purpose} disabled={readOnly || Boolean(prompt.id)} onChange={event => change("purpose", event.target.value)}>{purposes.map(item => <option key={item} value={item}>{t(`prompts.purpose.${item}`)}</option>)}</select>}</Field>
      </div>
      <Field label="prompts.descriptionField">{props => <input {...props} maxLength={1000} readOnly={readOnly} value={value.description} onChange={event => change("description", event.target.value)} />}</Field>
      <div className="prompt-editor-body">
        <Field label="prompts.body">{props => <textarea {...props} ref={body} className="prompt-text" rows={20} required maxLength={32000} readOnly={readOnly} spellCheck={false} aria-invalid={error?.field === "body" || problems.length ? "true" : undefined} value={value.body} onChange={event => change("body", event.target.value)} />}</Field>
        <aside className="prompt-legend" aria-label={t("prompts.legend")}>
          <h3>{t("prompts.legend")}</h3>
          <p className="muted">{t(readOnly ? "prompts.legend.readOnly" : "prompts.legend.help")}</p>
          <ul>{available.map(parameter => <li key={parameter.name}>
            {readOnly ? <code>{"{" + parameter.name + "}"}</code> : <button type="button" className="parameter-chip" title={t("prompts.insert", { name: parameter.name })} onClick={() => insert(parameter.name)}><code>{"{" + parameter.name + "}"}</code></button>}
            {parameter.requiredFor.includes(value.purpose) && <span className="badge">{t("prompts.required")}</span>}
            <p>{parameter.description}</p>
          </li>)}</ul>
          <p className="muted">{t("prompts.legend.braces")}</p>
        </aside>
      </div>
      {!readOnly && problems.length > 0 && <ul className="prompt-problems" role="status">{problems.map(problem => <li key={problem.kind + problem.name}>{t(`prompts.problem.${problem.kind}`, { name: "{" + problem.name + "}", purpose: t(`prompts.purpose.${value.purpose}`) })}</li>)}</ul>}
      {!readOnly && <p className="muted">{t("prompts.formatWarning")}</p>}
      {error && <ErrorPanel error={error} />}
      <footer className="dialog-actions">
        <Button label="common.close" type="button" disabled={busy} onClick={onClose} />
        {readOnly
          ? <Button label="prompts.duplicateToEdit" type="button" className="primary" onClick={onDuplicate} />
          : <>{prompt.id && <Button label="prompts.duplicate" type="button" disabled={busy} onClick={onDuplicate} />}<Button label="common.save" type="submit" className="primary" disabled={busy || problems.length > 0 || !value.name.trim() || !value.body.trim()} /></>}
      </footer>
    </form>
  </Modal>;
}
