import { useState } from "react";
import { useTranslation } from "react-i18next";
import ActionMenu from "./ActionMenu.jsx";
import CopyButton from "./CopyButton.jsx";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

export const expiryChoices = [["never", 0], ["days30", 30], ["days90", 90], ["year", 365]];

/** Expiry time for a choice of days from now, or null for a credential that never expires. */
export function expiryFor(days, now = Date.now()) {
  return days > 0 ? new Date(now + days * 86400000).toISOString() : null;
}

/** Bearer-token credentials for the signed-in user, or for another user when an administrator passes userId. */
export default function AuthCredentials({ api, notify, userId = "", tenantId = "" }) {
  const { t } = useTranslation();
  const { data, error, reload } = useData("auth-credentials:" + tenantId + ":" + userId, () => api.credentials(userId, tenantId));
  const [creating, setCreating] = useState(null);
  const [created, setCreated] = useState(null);
  const [renaming, setRenaming] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [revealing, setRevealing] = useState(null);
  const [revealed, setRevealed] = useState(null);
  const [failure, setFailure] = useState(null);
  const save = async (credential, changes) => {
    try { await api.updateCredential(credential.id, { name: credential.name, expiresUtc: credential.expiresUtc, active: credential.active, revision: credential.revision, ...changes }, userId, tenantId); await reload(); }
    catch (problem) { notify(problem); }
  };
  return <section className="page-section">
    <header><h2>{t("authCredentials.title")}</h2><Button label="authCredentials.add" icon="plus" className="primary" onClick={() => { setFailure(null); setCreating({ name: "", days: 0 }); }} /></header>
    <p className="muted">{t("authCredentials.description")}</p>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && data.length === 0 && <div className="empty-state">{t("authCredentials.empty")}</div>}
    {data && data.length > 0 && <div className="table-scroll"><table>
      <thead><tr><th scope="col">{t("authCredentials.name")}</th><th scope="col">{t("authCredentials.token")}</th><th scope="col">{t("authCredentials.status")}</th><th scope="col">{t("authCredentials.expires")}</th><th scope="col">{t("authCredentials.lastUsed")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{data.map(credential => <tr key={credential.id}>
        <td><strong>{credential.name}</strong>{credential.protected && <> <span className="badge" title={t("authCredentials.protected.help")}>{t("authCredentials.protected")}</span></>}</td>
        <td><code>{"••••" + credential.tokenLast4}</code></td>
        <td><span className={credential.active ? "badge success" : "badge"}>{t(credential.active ? "authCredentials.active" : "authCredentials.disabled")}</span></td>
        <td>{credential.expiresUtc ? <time>{formatDate(credential.expiresUtc)}</time> : t("authCredentials.never")}</td>
        <td>{credential.lastUsedUtc ? <time>{formatDate(credential.lastUsedUtc)}</time> : t("authCredentials.unused")}</td>
        <td className="actions-cell"><ActionMenu context={credential.name} items={[
          { label: "authCredentials.reveal", help: "authCredentials.reveal.help", onClick: () => { setFailure(null); setRevealing(credential); } },
          { label: "authCredentials.rename", onClick: () => setRenaming({ ...credential }) },
          { label: credential.active ? "authCredentials.disable" : "authCredentials.enable", hidden: credential.protected, onClick: () => save(credential, { active: !credential.active }) },
          { label: "common.delete", danger: true, hidden: credential.protected, onClick: () => { setFailure(null); setDeleting(credential); } },
        ]} /></td>
      </tr>)}</tbody>
    </table></div>}
    {creating && <Modal title="authCredentials.add" onClose={() => setCreating(null)}>
      <form onSubmit={async event => {
        event.preventDefault();
        try { const value = await api.createCredential({ name: creating.name.trim(), expiresUtc: expiryFor(creating.days), active: true, revision: 0 }, userId, tenantId); setCreating(null); setCreated(value); await reload(); }
        catch (problem) { setFailure(problem); }
      }}>
        <Field label="authCredentials.name">{props => <input {...props} data-autofocus required maxLength={128} value={creating.name} onChange={event => setCreating({ ...creating, name: event.target.value })} />}</Field>
        <Field label="authCredentials.expiry">{props => <select {...props} value={creating.days} onChange={event => setCreating({ ...creating, days: Number(event.target.value) })}>{expiryChoices.map(([key, days]) => <option key={key} value={days}>{t("authCredentials.expiry." + key)}</option>)}</select>}</Field>
        {failure && <ErrorPanel error={failure} />}
        <footer className="dialog-actions"><Button label="common.cancel" type="button" onClick={() => setCreating(null)} /><Button label="common.create" type="submit" className="primary" disabled={!creating.name.trim()} /></footer>
      </form>
    </Modal>}
    {created && <Modal title="authCredentials.created.title" onClose={() => setCreated(null)}>
      <p>{t("authCredentials.created.help")}</p>
      <div className="token-reveal"><code>{created.token}</code><CopyButton value={created.token} label="authCredentials.copy" /></div>
      <p className="muted">{t("authCredentials.created.usage")}</p>
      <footer className="dialog-actions"><Button label="authCredentials.created.done" className="primary" onClick={() => setCreated(null)} /></footer>
    </Modal>}
    {revealing && <Modal title="authCredentials.reveal.title" onClose={() => setRevealing(null)}>
      <p>{t("authCredentials.reveal.confirm", { name: revealing.name })}</p>
      {failure && <ErrorPanel error={failure} />}
      <footer className="dialog-actions"><Button label="common.cancel" onClick={() => setRevealing(null)} /><Button label="authCredentials.reveal" className="primary" onClick={async () => { try { setRevealed(await api.revealToken(revealing.id, userId, tenantId)); setRevealing(null); } catch (problem) { setFailure(problem); } }} /></footer>
    </Modal>}
    {revealed && <Modal title="authCredentials.revealed.title" onClose={() => setRevealed(null)}>
      <p>{t("authCredentials.revealed.help", { name: revealed.name })}</p>
      <div className="token-reveal"><code>{revealed.token}</code><CopyButton value={revealed.token} label="authCredentials.copy" /></div>
      <footer className="dialog-actions"><Button label="common.close" className="primary" onClick={() => setRevealed(null)} /></footer>
    </Modal>}
    {renaming && <Modal title="authCredentials.rename" onClose={() => setRenaming(null)}>
      <form onSubmit={async event => { event.preventDefault(); await save(renaming, { name: renaming.name.trim() }); setRenaming(null); }}>
        <Field label="authCredentials.name">{props => <input {...props} data-autofocus required maxLength={128} value={renaming.name} onChange={event => setRenaming({ ...renaming, name: event.target.value })} />}</Field>
        <footer className="dialog-actions"><Button label="common.cancel" type="button" onClick={() => setRenaming(null)} /><Button label="common.save" type="submit" className="primary" disabled={!renaming.name.trim()} /></footer>
      </form>
    </Modal>}
    {deleting && <Modal title="authCredentials.delete.title" onClose={() => setDeleting(null)}>
      <p>{t("authCredentials.delete.confirm", { name: deleting.name })}</p>
      {failure && <ErrorPanel error={failure} />}
      <footer className="dialog-actions"><Button label="common.cancel" onClick={() => setDeleting(null)} /><Button label="common.delete" className="danger" onClick={async () => { try { await api.deleteCredential(deleting.id, userId, tenantId); setDeleting(null); await reload(); } catch (problem) { setFailure(problem); } }} /></footer>
    </Modal>}
  </section>;
}
