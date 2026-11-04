import ModelChecksDialog from "./ModelChecksDialog.jsx";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import { useData } from "./useData.js";
import harnessDefaults from "../../../assets/harness-defaults.json";

const empty = {
  endpoints: { name: "", protocol: "ollama", baseUrl: "http://127.0.0.1:11434", timeoutSeconds: 300, maxConcurrentInvocations: 1, enabled: true, authBindings: [] },
  harnesses: { name: "", provider: "claude", executable: "claude", arguments: harnessDefaults.claude.arguments, workingDirectory: "", credentialId: "", environmentVariables: [], enabled: true },
  models: { name: "", endpointId: "", harnessId: "", model: "", contextMaxCharacters: 262144, maxOutputBytes: 67108864, generation: { maxOutputTokens: null, temperature: null, topP: null }, supportsTools: true, enabled: true },
  credentials: { name: "", kind: "token", secretValue: "" },
};
export function CatalogForm({ kind, initial, data, onSave, onClose }) {
  const { t } = useTranslation();
  const [value, setValue] = useState(() => structuredClone(initial ? { ...initial, ...(kind === "credentials" ? { secretValue: "" } : {}) } : empty[kind]));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const change = (key, next) => setValue(current => ({ ...current, [key]: next }));
  const field = (key, type = "text", options = {}) => <Field label={`catalog.${key}`}>{props => <input {...props} {...options} type={type} value={value[key] ?? ""} onChange={event => change(key, type === "number" ? Number(event.target.value) : event.target.value)} />}</Field>;
  const select = (key, choices, onChange) => <Field label={`catalog.${key}`}>{props => <select {...props} value={value[key]} onChange={event => onChange ? onChange(event.target.value) : change(key, event.target.value)}>{choices.map(([id, name]) => <option value={id} key={id}>{name}</option>)}</select>}</Field>;
  const generationField = (key, min, max, step) => <Field label={`catalog.${key}`}>{props => <input {...props} type="number" min={min} max={max} step={step} value={value.generation?.[key] ?? ""} onChange={event => change("generation", { ...value.generation, [key]: event.target.value === "" ? null : Number(event.target.value) })} />}</Field>;
  const checkbox = key => <Field label={`catalog.${key}`}>{props => <input {...props} type="checkbox" checked={value[key]} onChange={event => change(key, event.target.checked)} />}</Field>;
  const credentials = [["", t("catalog.none")], ...(data.credentials || []).filter(item => item.status === "ready" || item.id === value.credentialId).map(item => [item.id, `${item.name} (${item.kind})`])];
  const bind = (index, key, next) => change("authBindings", value.authBindings.map((item, position) => position === index ? { ...item, [key]: next } : item));
  return <Modal title={`catalog.${kind}`} wide onClose={() => !busy && onClose()}><form onSubmit={async event => {
    event.preventDefault(); setBusy(true); setError(null);
    try { await onSave(kind === "harnesses" ? { ...value, environmentVariables: (value.environmentVariables || []).filter(Boolean) } : value); change("secretValue", ""); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  }} autoComplete="off">
    {error && <ErrorPanel error={error} />}
    {field("name", "text", { required: true, maxLength: 200 })}
    {kind === "endpoints" && <>
      <div className="form-grid">{select("protocol", [["ollama", "Ollama native chat"], ["openai", "OpenAI-compatible chat completions"]])}{field("baseUrl", "url", { required: true })}{field("timeoutSeconds", "number", { min: 1, max: 3600, required: true })}{field("maxConcurrentInvocations", "number", { min: 1, max: 64, required: true })}{checkbox("enabled")}</div>
      <fieldset className="argument-editor"><legend>{t("catalog.authBindings")}</legend><p className="muted">{t("catalog.authHelp")}</p>
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
      <div className="form-grid">{select("provider", Object.entries(harnessDefaults).map(([id, preset]) => [id, preset.name]), provider => setValue(current => ({ ...current, provider, executable: harnessDefaults[provider].executable, arguments: [...harnessDefaults[provider].arguments] })))}{field("executable", "text", { required: true })}{field("workingDirectory")}{select("credentialId", credentials)}{checkbox("enabled")}</div>
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
      {select("kind", [["token", t("catalog.token")], ["claude_oauth", "Claude Code OAuth token"], ["codex_auth_json", "Codex auth.json"]])}
      <Field label="catalog.secretValue">{props => <input {...props} type="password" className="secret-input" autoComplete="new-password" spellCheck={false} required value={value.secretValue} onChange={event => change("secretValue", event.target.value)} />}</Field>
      <p className="muted">{t("catalog.secretHelp")}</p>
      <Field label="catalog.secretFile">{props => <input {...props} type="file" accept=".json,.txt" onChange={async event => { const file = event.target.files?.[0]; if (file && file.size <= 65536) change("secretValue", await file.text()); else setError(new Error(t("catalog.fileLimit"))); }} />}</Field>
    </>}
    <footer className="dialog-actions"><Button type="button" label="common.cancel" disabled={busy} onClick={onClose} /><Button type="submit" label="common.save" className="primary" disabled={busy} /></footer>
  </form></Modal>;
}
export default function CatalogPanel({ api, notify }) {
  const { t } = useTranslation();
  const [modelCheck, setModelCheck] = useState(null);
  const [keys, setKeys] = useState(null);
  const [rotating, setRotating] = useState(false);
  const [confirmRetire, setConfirmRetire] = useState(false);
  const [kind, setKind] = useState("endpoints");
  const [editing, setEditing] = useState(null);
  const [discovery, setDiscovery] = useState(null);
  const [preview, setPreview] = useState(null);
  const [checking, setChecking] = useState("");
  const { data, error, reload } = useData("catalog", async () => Object.fromEntries(await Promise.all(Object.keys(empty).map(async key => [key, await api.configuration(key)]))));
  return <section className="catalog-panel" aria-label={t("catalog.title")}>
    <h2>{t("catalog.title")}</h2><p className="muted">{t("catalog.intro")}</p>
    {error && <ErrorPanel error={error} retry={reload} />}
    <div className="catalog-toolbar"><Field label="catalog.resource">{props => <select {...props} value={kind} onChange={event => setKind(event.target.value)}>{Object.keys(empty).map(key => <option key={key} value={key}>{t(`catalog.${key}`)}</option>)}</select>}</Field><Button label="catalog.add" onClick={() => setEditing({})} disabled={!data} /></div>
    {kind === "credentials" && <Button label="catalog.keys" onClick={async () => { try { setKeys(await api.credentialKeys()); } catch (failure) { notify(failure); } }} />}
    {keys && <Modal title="catalog.keys" onClose={() => !rotating && setKeys(null)}><p>{t("catalog.keyHelp")}</p><dl className="server-details"><dt>{t("catalog.activeKey")}</dt><dd><code>{keys.activeKeyId}</code></dd><dt>{t("catalog.retainedKeys")}</dt><dd>{keys.retainedKeyCount}</dd></dl>{keys.reencryptedCredentials > 0 && <p role="status">{t("catalog.keyRotated", { count: keys.reencryptedCredentials })}</p>}{keys.skippedCredentials > 0 && <p className="notice" role="status">{t("catalog.keySkipped", { count: keys.skippedCredentials })}</p>}{keys.retiredKeyCount > 0 && <p role="status">{t("catalog.keysRetired", { count: keys.retiredKeyCount })}</p>}{confirmRetire && <p className="notice warning" role="alert">{t("catalog.retireWarning", { count: keys.retainedKeyCount - 1 })}</p>}<footer className="dialog-actions"><Button label="common.close" disabled={rotating} onClick={() => { setKeys(null); setConfirmRetire(false); }} />{keys.retainedKeyCount > 1 && <Button label={confirmRetire ? "catalog.retireConfirm" : "catalog.retireKeys"} className={confirmRetire ? "danger" : ""} disabled={rotating} onClick={async () => { if (!confirmRetire) { setConfirmRetire(true); return; } setRotating(true); try { setKeys(await api.retireCredentialKeys(keys.activeKeyId)); setConfirmRetire(false); } catch (failure) { notify(failure); } finally { setRotating(false); } }} />}<Button label="catalog.rotateKey" className="primary" disabled={rotating} onClick={async () => { setRotating(true); setConfirmRetire(false); try { setKeys(await api.rotateCredentialKey(keys.activeKeyId)); } catch (failure) { notify(failure); } finally { setRotating(false); } }} /></footer></Modal>}
    {data && data[kind].length === 0 && <p className="empty-state">{t("catalog.empty")}</p>}
    <ul className="catalog-list">{data?.[kind].map(item => <li key={item.id}>
      <div><strong>{item.name}</strong><p className="muted">{kind === "credentials" ? <>{t(`credential.kind.${item.kind}`, { defaultValue: item.kind })} · <span className={item.status === "ready" ? "" : "status-attention"} title={item.status === "refresh_uncertain" ? t("credential.status.refresh_uncertain.help") : undefined}>{t(`credential.status.${item.status}`, { defaultValue: item.status })}</span></> : `${item.protocol || item.provider || item.model} · ${t(item.enabled ? "catalog.on" : "catalog.off")}`}</p></div>
      <div className="row-actions">{kind === "endpoints" && <Button label="catalog.preview" onClick={async () => { try { setPreview(await api.previewEndpoint(item.id)); } catch (failure) { notify(failure); } }} />}{kind === "models" && <Button label="modelCheck.title" onClick={() => setModelCheck(item)} />}{kind === "endpoints" && <Button label={checking === item.id ? "catalog.checking" : "catalog.discover"} disabled={!item.enabled || Boolean(checking)} onClick={async () => { setChecking(item.id); try { setDiscovery({ ...await api.discoverEndpoint(item.id), name: item.name }); } catch (failure) { notify(failure); } finally { setChecking(""); } }} />}<Button label={kind === "credentials" ? "catalog.replace" : "common.edit"} onClick={() => setEditing(item)} />{kind === "credentials" && item.status !== "disabled" && <Button label="catalog.disable" onClick={async () => { try { await api.disableCredential(item.id, item.revision); await reload(); } catch (failure) { notify(failure); } }} />}</div>
    </li>)}</ul>
    {modelCheck && <ModelChecksDialog api={api} model={modelCheck} notify={notify} onClose={() => setModelCheck(null)} />}
    {discovery && <Modal title="catalog.discoveryTitle" onClose={() => setDiscovery(null)}><h3>{discovery.name}</h3><p>{t("catalog.discoveryHelp")}</p><p className="muted">{new Date(discovery.checkedUtc).toLocaleString()} · {t("catalog.revision", { revision: discovery.revision })}</p>{discovery.models.length ? <ul className="discovered-models">{discovery.models.map(model => <li key={model}><code>{model}</code></li>)}</ul> : <p>{t("catalog.noModels")}</p>}{discovery.truncated && <p>{t("catalog.discoveryTruncated")}</p>}<footer className="dialog-actions"><Button label="common.close" onClick={() => setDiscovery(null)} /></footer></Modal>}
    {preview && <Modal title="catalog.preview" onClose={() => setPreview(null)} wide><p>{t("catalog.previewHelp")}</p><dl className="server-details"><dt>POST</dt><dd className="source-digest"><code>{preview.generationUrl}</code></dd><dt>GET</dt><dd className="source-digest"><code>{preview.discoveryUrl}</code></dd></dl><h3>{t("catalog.previewHeaders")}</h3>{preview.headerNames.length ? <ul>{preview.headerNames.map(name => <li key={name}><code>{name}: [redacted]</code></li>)}</ul> : <p>{t("common.none")}</p>}<h3>{t("catalog.previewQuery")}</h3>{preview.queryNames.length ? <ul>{preview.queryNames.map(name => <li key={name}><code>{name}=[redacted]</code></li>)}</ul> : <p>{t("common.none")}</p>}<Button label="common.close" onClick={() => setPreview(null)} /></Modal>}
    {editing && <CatalogForm kind={kind} initial={editing.id ? editing : null} data={data} onClose={() => setEditing(null)} onSave={async value => { await api.saveConfiguration(kind, value); setEditing(null); await reload(); }} />}
  </section>;
}
