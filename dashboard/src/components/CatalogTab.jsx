import CopyButton from "./CopyButton.jsx";
import ModelChecksDialog from "./ModelChecksDialog.jsx";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import Markdown from "react-markdown";
import harnessDefaults from "../../../assets/harness-defaults.json";

const credentialKinds = ["claude_oauth", "codex_auth_json", "token"];
/** Steps per credential kind. Commands are literal shell text and are not translated. */
const credentialGuides = {
  claude_oauth: [{ text: "1", command: "claude setup-token" }, { text: "2" }, { text: "3" }],
  codex_auth_json: [{ text: "1", command: "mkdir -p ~/.codex-arguebot && CODEX_HOME=~/.codex-arguebot codex login", alternative: { text: "1b", command: "mkdir -p ~/.codex-arguebot && printenv OPENAI_API_KEY | CODEX_HOME=~/.codex-arguebot codex login --with-api-key" } }, { text: "2", command: "pbcopy < ~/.codex-arguebot/auth.json" }, { text: "3" }],
  token: [{ text: "1" }, { text: "2" }, { text: "3" }],
};
function CommandBox({ command }) {
  const { t } = useTranslation();
  return <div className="command-box"><code aria-label={t("credential.guide.command")}>{command}</code><CopyButton value={command} label="credential.guide.copyCommand" iconOnly /></div>;
}
const harnessCredentialKind = { claude: "claude_oauth", codex: "codex_auth_json" };
const empty = {
  endpoints: { name: "", protocol: "ollama", baseUrl: "http://127.0.0.1:11434", timeoutSeconds: 300, maxConcurrentInvocations: 1, enabled: true, authBindings: [] },
  harnesses: { name: "", provider: "claude", executable: "claude", arguments: harnessDefaults.claude.arguments, workingDirectory: "", credentialId: "", environmentVariables: [], enabled: true },
  models: { name: "", endpointId: "", harnessId: "", model: "", contextMaxCharacters: 262144, maxOutputBytes: 67108864, generation: { maxOutputTokens: null, temperature: null, topP: null }, supportsTools: true, enabled: true },
  credentials: { name: "", kind: "claude_oauth", secretValue: "" },
};
/** The catalog record kinds, in the order the Participants tabs show them. */
export const catalogKinds = ["models", "harnesses", "endpoints", "credentials"];
export function CatalogForm({ kind, initial, data, onSave, onClose, preset }) {
  const { t } = useTranslation();
  const [value, setValue] = useState(() => structuredClone(initial ? { ...initial, ...(kind === "credentials" ? { secretValue: "" } : {}) } : { ...empty[kind], ...(preset || {}) }));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const change = (key, next) => setValue(current => ({ ...current, [key]: next }));
  const field = (key, type = "text", options = {}) => <Field label={`catalog.${key}`}>{props => <input {...props} {...options} type={type} value={value[key] ?? ""} onChange={event => change(key, type === "number" ? Number(event.target.value) : event.target.value)} />}</Field>;
  const select = (key, choices, onChange) => <Field label={`catalog.${key}`}>{props => <select {...props} value={value[key]} onChange={event => onChange ? onChange(event.target.value) : change(key, event.target.value)}>{choices.map(([id, name]) => <option value={id} key={id}>{name}</option>)}</select>}</Field>;
  const generationField = (key, min, max, step) => <Field label={`catalog.${key}`}>{props => <input {...props} type="number" min={min} max={max} step={step} value={value.generation?.[key] ?? ""} onChange={event => change("generation", { ...value.generation, [key]: event.target.value === "" ? null : Number(event.target.value) })} />}</Field>;
  const checkbox = key => <Field label={`catalog.${key}`}>{props => <input {...props} type="checkbox" checked={value[key]} onChange={event => change(key, event.target.checked)} />}</Field>;
  const usable = accepts => [["", t("catalog.none")], ...(data.credentials || []).filter(item => (item.status === "ready" || item.id === value.credentialId || item.id === accepts.current) && accepts.kinds.includes(item.kind)).map(item => [item.id, `${item.name} (${t(`credential.kind.${item.kind}`, { defaultValue: item.kind })})`])];
  const credentials = usable({ kinds: ["token"], current: "" });
  const harnessKind = harnessCredentialKind[value.provider] || "";
  const harnessCredentials = usable({ kinds: harnessKind ? [harnessKind] : [], current: value.credentialId });
  const bind = (index, key, next) => change("authBindings", value.authBindings.map((item, position) => position === index ? { ...item, [key]: next } : item));
  return <Modal title={initial ? `catalog.edit.${kind}` : `catalog.add.${kind}`} wide onClose={() => !busy && onClose()}><form onSubmit={async event => {
    event.preventDefault(); setBusy(true); setError(null);
    try { await onSave(kind === "harnesses" ? { ...value, environmentVariables: (value.environmentVariables || []).filter(Boolean) } : value); change("secretValue", ""); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  }} autoComplete="off">
    {error && <ErrorPanel error={error} />}
    {field("name", "text", { required: true, maxLength: 200 })}
    {kind === "endpoints" && <>
      <div className="form-grid">{select("protocol", [["ollama", "Ollama native chat"], ["openai", "OpenAI-compatible chat completions"]])}{field("baseUrl", "url", { required: true })}{field("timeoutSeconds", "number", { min: 1, max: 3600, required: true })}{field("maxConcurrentInvocations", "number", { min: 1, max: 64, required: true })}{checkbox("enabled")}</div>
      <fieldset className="argument-editor"><legend>{t("catalog.authBindings")}</legend><p className="muted">{t("catalog.authHelp")}</p>{credentials.length === 1 && <p className="muted">{t("catalog.authAddFirst")}</p>}
        {value.authBindings.map((binding, index) => <div className="binding-editor" key={index}>
          <Field label="catalog.bindingKind">{props => <select {...props} value={binding.kind} onChange={event => bind(index, "kind", event.target.value)}>{["bearer", "api_key", "header", "query"].map(item => <option key={item} value={item}>{t(`catalog.auth.${item}`)}</option>)}</select>}</Field>
          {binding.kind !== "bearer" && <Field label="catalog.bindingName">{props => <input {...props} required value={binding.name} onChange={event => bind(index, "name", event.target.value)} />}</Field>}
          <Field label="catalog.credentialId">{props => <select {...props} value={binding.credentialId} onChange={event => { bind(index, "credentialId", event.target.value); }} >{credentials.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select>}</Field>
          <Field label="catalog.environmentVariable">{props => <input {...props} value={binding.environmentVariable} onChange={event => bind(index, "environmentVariable", event.target.value)} />}</Field>
          {binding.kind === "header" && <Field label="catalog.staticValue">{props => <input {...props} value={binding.value} onChange={event => bind(index, "value", event.target.value)} />}</Field>}
          <Button type="button" label="common.remove" onClick={() => change("authBindings", value.authBindings.filter((_, position) => index !== position))} />
        </div>)}
        <Button type="button" label="catalog.addBinding" disabled={value.authBindings.length >= 32} onClick={() => change("authBindings", [...value.authBindings, { kind: "bearer", name: "", credentialId: "", environmentVariable: "", value: "" }])} />
      </fieldset>
    </>}
    {kind === "harnesses" && <>
      <div className="form-grid">{select("provider", Object.entries(harnessDefaults).map(([id, preset]) => [id, preset.name]), provider => setValue(current => ({ ...current, provider, executable: harnessDefaults[provider].executable, arguments: [...harnessDefaults[provider].arguments] })))}{field("executable", "text", { required: true })}{field("workingDirectory")}{harnessKind ? select("credentialId", harnessCredentials) : null}{checkbox("enabled")}</div>
      <p className="muted field-note">{t(harnessKind ? (value.credentialId ? "catalog.harnessCredential.stored" : "catalog.harnessCredential.none") : "catalog.harnessCredential.unsupported")} {harnessKind && harnessCredentials.length === 1 && t("catalog.harnessCredential.addFirst", { kind: t(`credential.kind.${harnessKind}`) })}</p>
      <Field label="catalog.environmentVariables">{props => <input {...props} value={(value.environmentVariables || []).join(", ")} onChange={event => change("environmentVariables", event.target.value.split(",").map(name => name.trim()))} />}</Field>
      <fieldset className="argument-editor"><legend>{t("profile.arguments")}</legend><p className="muted">{t("profile.arguments.help")}</p>{value.arguments.map((argument, index) => <div className="argument-row" key={index}><input aria-label={t("profile.argument", { number: index + 1 })} value={argument} onChange={event => change("arguments", value.arguments.map((item, position) => position === index ? event.target.value : item))} /><Button type="button" label="common.remove" onClick={() => change("arguments", value.arguments.filter((_, position) => position !== index))} /></div>)}<Button type="button" label="profile.addArgument" onClick={() => change("arguments", [...value.arguments, ""])} /></fieldset>
    </>}
    {kind === "models" && <div className="form-grid">
      {select("endpointId", [["", t("catalog.none")], ...(data.endpoints || []).map(item => [item.id, item.name])], endpointId => setValue(current => ({ ...current, endpointId, harnessId: endpointId ? "" : current.harnessId })))}
      {select("harnessId", [["", t("catalog.none")], ...(data.harnesses || []).map(item => [item.id, item.name])], harnessId => setValue(current => ({ ...current, harnessId, endpointId: harnessId ? "" : current.endpointId, generation: harnessId ? { maxOutputTokens: null, temperature: null, topP: null } : current.generation })))}
      {field("model", "text", { required: Boolean(value.endpointId) })}{field("contextMaxCharacters", "number", { min: 8192, max: 16777216, required: true })}{field("maxOutputBytes", "number", { min: 1024, max: 67108864, required: true })}{checkbox("supportsTools")}{checkbox("enabled")}
      {value.endpointId && <fieldset className="generation-settings"><legend>{t("catalog.generation")}</legend><p className="muted">{t("catalog.generationHelp")}</p>{generationField("maxOutputTokens", 1, 1048576, 1)}{generationField("temperature", 0, 2, "any")}{generationField("topP", 0, 1, "any")}</fieldset>}
    </div>}
    {kind === "credentials" && <>
      <Field label="catalog.kind">{props => <select {...props} value={value.kind} disabled={Boolean(initial)} onChange={event => change("kind", event.target.value)}>{credentialKinds.map(item => <option key={item} value={item}>{t(`credential.kind.${item}`)}</option>)}</select>}</Field>
      <section className="credential-guide" aria-label={t("credential.guide.title")}>
        <h3>{t("credential.guide.title")}</h3>
        <ol>{credentialGuides[value.kind].map(step => <li key={step.text}><Markdown>{t(`credential.guide.${value.kind}.${step.text}`)}</Markdown>{step.command && <CommandBox command={step.command} />}{step.alternative && <><Markdown>{t(`credential.guide.${value.kind}.${step.alternative.text}`)}</Markdown><CommandBox command={step.alternative.command} /></>}</li>)}</ol>
      </section>
      {value.kind === "codex_auth_json"
        ? <Field label="catalog.secretFile">{props => <input {...props} type="file" accept=".json,application/json" onChange={async event => { const file = event.target.files?.[0]; if (file && file.size <= 65536) change("secretValue", await file.text()); else setError(new Error(t("catalog.fileLimit"))); }} />}</Field>
        : null}
      <Field label={value.kind === "codex_auth_json" ? "catalog.secretValue.json" : "catalog.secretValue"}>{props => <input {...props} type="password" className="secret-input" autoComplete="new-password" spellCheck={false} required value={value.secretValue} onChange={event => change("secretValue", event.target.value)} />}</Field>
      {value.kind !== "codex_auth_json" && <Field label="catalog.secretFile">{props => <input {...props} type="file" accept=".txt,.json" onChange={async event => { const file = event.target.files?.[0]; if (file && file.size <= 65536) change("secretValue", (await file.text()).trim()); else setError(new Error(t("catalog.fileLimit"))); }} />}</Field>}
      {value.secretValue && <p className="muted" role="status">{t("catalog.secretReady", { count: value.secretValue.length })}</p>}
      <p className="muted">{t("catalog.secretHelp")}</p>
    </>}
    <footer className="dialog-actions"><Button type="button" label="common.cancel" disabled={busy} onClick={onClose} /><Button type="submit" label="common.save" className="primary" disabled={busy} /></footer>
  </form></Modal>;
}
function KeyDialog({ api, notify, initial, onClose }) {
  const { t } = useTranslation();
  const [keys, setKeys] = useState(initial);
  const [rotating, setRotating] = useState(false);
  const [confirmRetire, setConfirmRetire] = useState(false);
  return <Modal title="catalog.keys" onClose={() => !rotating && onClose()}>
    <p>{t("catalog.keyHelp")}</p>
    <dl className="server-details"><dt>{t("catalog.activeKey")}</dt><dd><code>{keys.activeKeyId}</code></dd><dt>{t("catalog.retainedKeys")}</dt><dd>{keys.retainedKeyCount}</dd></dl>
    {keys.reencryptedCredentials > 0 && <p role="status">{t("catalog.keyRotated", { count: keys.reencryptedCredentials })}</p>}
    {keys.skippedCredentials > 0 && <p className="notice" role="status">{t("catalog.keySkipped", { count: keys.skippedCredentials })}</p>}
    {keys.retiredKeyCount > 0 && <p role="status">{t("catalog.keysRetired", { count: keys.retiredKeyCount })}</p>}
    {confirmRetire && <p className="notice warning" role="alert">{t("catalog.retireWarning", { count: keys.retainedKeyCount - 1 })}</p>}
    <footer className="dialog-actions">
      <Button label="common.close" disabled={rotating} onClick={onClose} />
      {keys.retainedKeyCount > 1 && <Button label={confirmRetire ? "catalog.retireConfirm" : "catalog.retireKeys"} className={confirmRetire ? "danger" : ""} disabled={rotating} onClick={async () => { if (!confirmRetire) { setConfirmRetire(true); return; } setRotating(true); try { setKeys(await api.retireCredentialKeys(keys.activeKeyId)); setConfirmRetire(false); } catch (failure) { notify(failure); } finally { setRotating(false); } }} />}
      <Button label="catalog.rotateKey" className="primary" disabled={rotating} onClick={async () => { setRotating(true); setConfirmRetire(false); try { setKeys(await api.rotateCredentialKey(keys.activeKeyId)); } catch (failure) { notify(failure); } finally { setRotating(false); } }} />
    </footer>
  </Modal>;
}

/** Names of the harnesses and endpoints that reference a credential. */
function usedBy(data, id) {
  return [...(data.harnesses || []).filter(item => item.credentialId === id), ...(data.endpoints || []).filter(item => (item.authBindings || []).some(binding => binding.credentialId === id))].map(item => item.name);
}

/** One reusable configuration kind as a table with its add, edit and inspection actions. */
export default function CatalogTab({ api, notify, kind, data, error, reload }) {
  const { t } = useTranslation();
  const [modelCheck, setModelCheck] = useState(null);
  const [keys, setKeys] = useState(null);
  const [editing, setEditing] = useState(null);
  const [discovery, setDiscovery] = useState(null);
  const [preview, setPreview] = useState(null);
  const [checking, setChecking] = useState("");
  const items = data?.[kind] || [];
  const nameOf = (list, id) => (data?.[list] || []).find(item => item.id === id)?.name;
  const enabled = item => <span className={item.enabled ? "badge" : "badge muted-badge"}>{t(item.enabled ? "catalog.on" : "catalog.off")}</span>;
  const columns = {
    credentials: [["catalog.column.kind", item => t(`credential.kind.${item.kind}`, { defaultValue: item.kind })], ["catalog.column.status", item => <span className={item.status === "ready" ? "badge" : "badge status-attention"} title={item.status === "refresh_uncertain" ? t("credential.status.refresh_uncertain.help") : undefined}>{t(`credential.status.${item.status}`, { defaultValue: item.status })}</span>], ["catalog.column.usedBy", item => { const names = usedBy(data, item.id); return names.length ? names.join(", ") : <span className="muted">{t("catalog.unused")}</span>; }]],
    harnesses: [["catalog.column.program", item => <><span>{harnessDefaults[item.provider]?.name || item.provider}</span> <code>{item.executable}</code></>], ["catalog.column.login", item => harnessCredentialKind[item.provider] ? (item.credentialId ? nameOf("credentials", item.credentialId) || item.credentialId : <span className="muted">{t("catalog.localLogin")}</span>) : <span className="muted">{t("catalog.environmentOnly")}</span>], ["catalog.column.enabled", enabled]],
    endpoints: [["catalog.column.protocol", item => item.protocol === "openai" ? "OpenAI-compatible" : "Ollama"], ["catalog.column.address", item => <code>{item.baseUrl}</code>], ["catalog.column.auth", item => item.authBindings?.length ? t("catalog.bindingCount", { count: item.authBindings.length }) : <span className="muted">{t("catalog.noAuth")}</span>], ["catalog.column.enabled", enabled]],
    models: [["catalog.column.model", item => item.model ? <code>{item.model}</code> : <span className="muted">{t("catalog.harnessDefault")}</span>], ["catalog.column.runsOn", item => nameOf("endpoints", item.endpointId) || nameOf("harnesses", item.harnessId) || <span className="muted">{t("catalog.unassigned")}</span>], ["catalog.column.enabled", enabled]],
  }[kind];
  return <section className="catalog-tab" aria-label={t(`catalog.${kind}`)}>
    <div className="tab-toolbar">
      <p className="muted">{t(`catalog.intro.${kind}`)}</p>
      <div className="row-actions">
        {kind === "credentials" && <Button label="catalog.keys" onClick={async () => { try { setKeys(await api.credentialKeys()); } catch (failure) { notify(failure); } }} />}
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
        <Button label={`catalog.add.${kind}`} icon="plus" className="primary" onClick={() => setEditing({})} disabled={!data} />
      </div>
    </div>
    {kind === "credentials" && <p className="notice">{t("catalog.dockerNote")}</p>}
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && items.length === 0 && <div className="empty-state">{t(`catalog.empty.${kind}`)}</div>}
    {items.length > 0 && <div className="table-scroll"><table>
      <thead><tr><th scope="col">{t("catalog.name")}</th>{columns.map(([label]) => <th scope="col" key={label}>{t(label)}</th>)}<th scope="col">{t("common.actions")}</th></tr></thead>
      <tbody>{items.map(item => <tr key={item.id}>
        <td><strong>{item.name}</strong></td>
        {columns.map(([label, render]) => <td key={label}>{render(item)}</td>)}
        <td><div className="row-actions">
          {kind === "endpoints" && <Button label="catalog.preview" onClick={async () => { try { setPreview(await api.previewEndpoint(item.id)); } catch (failure) { notify(failure); } }} />}
          {kind === "endpoints" && <Button label={checking === item.id ? "catalog.checking" : "catalog.discover"} disabled={!item.enabled || Boolean(checking)} onClick={async () => { setChecking(item.id); try { setDiscovery({ ...await api.discoverEndpoint(item.id), name: item.name }); } catch (failure) { notify(failure); } finally { setChecking(""); } }} />}
          {kind === "models" && <Button label="modelCheck.title" onClick={() => setModelCheck(item)} />}
          <Button label={kind === "credentials" ? "catalog.replace" : "common.edit"} onClick={() => setEditing(item)} />
          {kind === "credentials" && item.status !== "disabled" && <Button label="catalog.disable" onClick={async () => { try { await api.disableCredential(item.id, item.revision); await reload(); } catch (failure) { notify(failure); } }} />}
        </div></td>
      </tr>)}</tbody>
    </table></div>}
    {keys && <KeyDialog api={api} notify={notify} initial={keys} onClose={() => setKeys(null)} />}
    {modelCheck && <ModelChecksDialog api={api} model={modelCheck} notify={notify} onClose={() => setModelCheck(null)} />}
    {discovery && <Modal title="catalog.discoveryTitle" onClose={() => setDiscovery(null)}><h3>{discovery.name}</h3><p>{t("catalog.discoveryHelp")}</p><p className="muted">{new Date(discovery.checkedUtc).toLocaleString()} · {t("catalog.revision", { revision: discovery.revision })}</p>{discovery.models.length ? <ul className="discovered-models">{discovery.models.map(model => <li key={model}><code>{model}</code></li>)}</ul> : <p>{t("catalog.noModels")}</p>}{discovery.truncated && <p>{t("catalog.discoveryTruncated")}</p>}<footer className="dialog-actions"><Button label="common.close" onClick={() => setDiscovery(null)} /></footer></Modal>}
    {preview && <Modal title="catalog.preview" onClose={() => setPreview(null)} wide><p>{t("catalog.previewHelp")}</p><dl className="server-details"><dt>POST</dt><dd className="source-digest"><code>{preview.generationUrl}</code></dd><dt>GET</dt><dd className="source-digest"><code>{preview.discoveryUrl}</code></dd></dl><h3>{t("catalog.previewHeaders")}</h3>{preview.headerNames.length ? <ul>{preview.headerNames.map(name => <li key={name}><code>{name}: [redacted]</code></li>)}</ul> : <p>{t("common.none")}</p>}<h3>{t("catalog.previewQuery")}</h3>{preview.queryNames.length ? <ul>{preview.queryNames.map(name => <li key={name}><code>{name}=[redacted]</code></li>)}</ul> : <p>{t("common.none")}</p>}<footer className="dialog-actions"><Button label="common.close" onClick={() => setPreview(null)} /></footer></Modal>}
    {editing && <CatalogForm kind={kind} initial={editing.id ? editing : null} data={data} onClose={() => setEditing(null)} onSave={async value => { await api.saveConfiguration(kind, value); setEditing(null); await reload(); }} />}
  </section>;
}
