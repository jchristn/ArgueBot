import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { formatDate } from "../i18n/index.js";
import { byteLength, formatSize, parseTags, validResourceName } from "../utils/resources.js";
import ResourceHistory from "./ResourceHistory.jsx";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";

const maxBytes = 262144;

/** Form state for a resource; tags are edited as comma-separated text. */
export function resourceDraft(resource) {
  return {
    id: resource?.id || "", name: resource?.name || "", title: resource?.title || "", description: resource?.description || "",
    tags: (resource?.tags || []).join(", "), body: resource?.body || "", locked: Boolean(resource?.locked), revision: resource?.revision || 0,
  };
}

/** The request body for saving a draft. */
export function resourceRequest(draft) {
  return { id: draft.id, name: draft.name.trim(), title: draft.title.trim(), description: draft.description.trim(), tags: parseTags(draft.tags), body: draft.body, locked: draft.locked, revision: draft.revision };
}

/** Create or edit a resource: markdown with preview, metadata, lock, history and export. Saving sends the revision and explains a conflict. */
export default function ResourceEditor({ api, resource, initialRevision = 0, onSaved, onClose }) {
  const { t } = useTranslation();
  const [current, setCurrent] = useState(resource?.id ? resource : null);
  const [draft, setDraft] = useState(() => resourceDraft(resource));
  const [tab, setTab] = useState(initialRevision ? "history" : "edit");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [conflict, setConflict] = useState(null);
  const [loading, setLoading] = useState(Boolean(resource?.id && resource.body === undefined));
  useEffect(() => {
    // Lists carry summaries without bodies; load the full resource before editing.
    if (!resource?.id || resource.body !== undefined) return;
    let active = true;
    api.resource(resource.id).then(value => { if (active) { setCurrent(value); setDraft(resourceDraft(value)); } }).catch(failure => active && setError(failure)).finally(() => active && setLoading(false));
    return () => { active = false; };
  }, [api, resource]);
  const change = (key, value) => { setDraft(previous => ({ ...previous, [key]: value })); setError(null); };
  const bytes = byteLength(draft.body);
  const nameValid = validResourceName(draft.name);
  const save = async event => {
    event.preventDefault();
    setBusy(true); setError(null); setConflict(null);
    try {
      const saved = await api.saveResource(resourceRequest(draft));
      setCurrent(saved); setDraft(resourceDraft(saved));
      await onSaved(saved);
    } catch (failure) {
      if (failure.code === "revision_conflict") setConflict({ draft, error: failure });
      else setError(failure);
    } finally { setBusy(false); }
  };
  const reloadLatest = async () => {
    try { const latest = await api.resource(draft.id); setCurrent(latest); setDraft(resourceDraft(latest)); setConflict(null); }
    catch (failure) { setError(failure); }
  };
  const download = async () => {
    try {
      const text = await api.exportResource(draft.id);
      const link = document.createElement("a");
      link.href = URL.createObjectURL(new Blob([text], { type: "text/markdown" }));
      link.download = draft.name.replaceAll("/", "_") + ".md";
      link.click();
      URL.revokeObjectURL(link.href);
    } catch (failure) { setError(failure); }
  };
  const reverted = async value => { setCurrent(value); setDraft(resourceDraft(value)); setTab("edit"); await onSaved(value); };
  return <Modal title={draft.id ? "resources.editTitle" : "resources.addTitle"} wide className="resource-editor" onClose={() => !busy && onClose()}>
    {loading ? <p>{t("common.loading")}</p> : <form onSubmit={save}>
      {current && <p className="muted resource-meta">
        {t("resources.revisionLine", { revision: current.revision, time: formatDate(current.updatedUtc), name: current.updatedByName || current.updatedById })}
        {" "}<span className={current.updatedByKind === "agent" ? "badge warning" : "badge"} title={t(current.updatedByKind === "agent" ? "resources.agent.help" : "resources.user.help")}>{t(current.updatedByKind === "agent" ? "resources.agent" : "resources.user")}</span>
      </p>}
      <div className="form-grid">
        <Field label="resources.name">{props => <input {...props} data-autofocus={draft.id ? undefined : true} required maxLength={128} aria-invalid={draft.name && !nameValid ? "true" : undefined} value={draft.name} onChange={event => change("name", event.target.value)} />}</Field>
        <Field label="resources.titleField">{props => <input {...props} maxLength={200} value={draft.title} onChange={event => change("title", event.target.value)} />}</Field>
      </div>
      {draft.name && !nameValid && <p className="field-error" role="alert">{t("resources.nameInvalid")}</p>}
      <Field label="resources.descriptionField">{props => <input {...props} maxLength={500} value={draft.description} onChange={event => change("description", event.target.value)} />}</Field>
      <div className="form-grid">
        <Field label="resources.tagsField">{props => <input {...props} value={draft.tags} onChange={event => change("tags", event.target.value)} />}</Field>
        <label className="checkbox-label" title={t("resources.locked.help")}><input type="checkbox" checked={draft.locked} onChange={event => change("locked", event.target.checked)} /> {t("resources.locked")}</label>
      </div>
      <div className="page-tabs-list" role="tablist">
        {["edit", "preview", ...(draft.id ? ["history"] : [])].map(item => <button key={item} type="button" role="tab" aria-selected={tab === item} className={tab === item ? "page-tab active" : "page-tab"} onClick={() => setTab(item)}>{t("resources.tab." + item)}</button>)}
      </div>
      {tab === "edit" && <Field label="resources.body">{props => <textarea {...props} className="prompt-text resource-text" rows={20} spellCheck={false} value={draft.body} onChange={event => change("body", event.target.value)} />}</Field>}
      {tab === "preview" && <div className="markdown resource-preview">{draft.body ? <Markdown remarkPlugins={[remarkGfm]}>{draft.body}</Markdown> : <p className="muted">{t("resources.previewEmpty")}</p>}</div>}
      {tab === "history" && current && <ResourceHistory api={api} resource={current} initialRevision={initialRevision} onReverted={reverted} />}
      <p className={bytes > maxBytes ? "field-error" : "muted"}>{t("resources.size", { size: formatSize(bytes), limit: formatSize(maxBytes) })}</p>
      {conflict && <div className="notice warning" role="alert">
        <p>{t("resources.conflict")}</p>
        <details><summary>{t("resources.conflict.yourText")}</summary><pre className="resource-draft">{conflict.draft.body}</pre></details>
        <Button label="resources.reloadLatest" type="button" onClick={reloadLatest} />
      </div>}
      {error && <ErrorPanel error={error} />}
      <footer className="dialog-actions">
        <Button label="common.close" type="button" disabled={busy} onClick={onClose} />
        {draft.id && <Button label="resources.export" type="button" icon="download" disabled={busy} onClick={download} />}
        <Button label="common.save" type="submit" className="primary" disabled={busy || !nameValid || bytes > maxBytes || tab === "history"} />
      </footer>
    </form>}
  </Modal>;
}
