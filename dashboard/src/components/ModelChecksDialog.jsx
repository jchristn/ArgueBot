import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import { useData } from "./useData.js";
import { randomId } from "../utils/random-id.js";

export default function ModelChecksDialog({ api, model, notify, onClose }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [checkTools, setCheckTools] = useState(false);
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState(randomId);
  const { data, error, reload } = useData(`${model.id}:${page}`, () => api.modelChecks(model.id, page));
  return <Modal title="modelCheck.title" wide onClose={() => !busy && onClose()}>
    <h3>{model.name}</h3><p>{t("modelCheck.help")}</p><p className="muted">{t("modelCheck.declared", { revision: model.revision, tools: t(model.supportsTools ? "common.yes" : "common.no") })}</p>
    {error && <ErrorPanel error={error} retry={reload} />}
    <Field label="modelCheck.tools">{props => <input {...props} type="checkbox" checked={checkTools} disabled={busy || !model.supportsTools} onChange={event => { setCheckTools(event.target.checked); setKey(randomId()); }} />}</Field>
    <Button label={busy ? "modelCheck.running" : "modelCheck.run"} disabled={busy || !model.enabled} onClick={async () => { setBusy(true); try { await api.checkModel(model.id, { checkTools, idempotencyKey: key }); setKey(randomId()); if (page === 1) await reload(); else setPage(1); } catch (failure) { notify(failure); } finally { setBusy(false); } }} />
    {data?.length === 0 && <p>{t("modelCheck.empty")}</p>}
    {data?.map(check => <article className="proposal" key={check.id}><strong>{t(`modelCheck.${check.state}`)}</strong><p className="muted">{new Date(check.createdUtc).toLocaleString()} · {t("modelCheck.revisions", { model: check.modelRevision, transport: check.transportRevision })}</p><dl className="server-details"><dt>{t("modelCheck.completion")}</dt><dd>{t(check.completionVerified ? "modelCheck.observed" : "modelCheck.notObserved")}</dd><dt>{t("modelCheck.toolResult")}</dt><dd>{t(check.toolsVerified ? "modelCheck.observed" : check.checkTools ? "modelCheck.notObserved" : "modelCheck.notChecked")}</dd><dt>{t("modelCheck.version")}</dt><dd>{check.executableVersion || t("modelCheck.notChecked")}</dd>{check.errorCode && <><dt>{t("common.error")}</dt><dd><code>{check.errorCode}</code></dd></>}</dl></article>)}
    <div className="button-group"><Button label="common.previous" disabled={busy || page <= 1} onClick={() => setPage(page - 1)} /><span>{t("memory.page", { page })}</span><Button label="common.next" disabled={busy || data?.length !== 25} onClick={() => setPage(page + 1)} /></div>
    <footer className="dialog-actions"><Button label="common.close" disabled={busy} onClick={onClose} /></footer>
  </Modal>;
}
