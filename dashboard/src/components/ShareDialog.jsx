import { useState } from "react";
import { useTranslation } from "react-i18next";
import ActionMenu from "./ActionMenu.jsx";
import CopyButton from "./CopyButton.jsx";
import DetailsDialog from "./DetailsDialog.jsx";
import ObjectRow from "./ObjectRow.jsx";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

export const shareLifetimes = [["hour", 1], ["day", 24], ["week", 168], ["month", 720], ["quarter", 2160]];

/** Absolute link for a share path, using the address this browser reached the service at. */
export function shareUrl(path, origin = globalThis.location?.origin || "") {
  return origin + path;
}

/** Create, copy and revoke time-bound public links to a conversation transcript. */
export default function ShareDialog({ api, conversation, onClose }) {
  const { t } = useTranslation();
  const { data, error, reload } = useData("shares:" + conversation.id, () => api.shares(conversation.id));
  const [hours, setHours] = useState(168);
  const [tools, setTools] = useState(false);
  const [created, setCreated] = useState(null);
  const [failure, setFailure] = useState(null);
  const [busy, setBusy] = useState(false);
  const [viewing, setViewing] = useState(null);
  const create = async () => {
    setBusy(true); setFailure(null);
    try { setCreated(await api.createShare(conversation.id, { expiresInHours: hours, includeToolCalls: tools })); await reload(); }
    catch (problem) { setFailure(problem); }
    finally { setBusy(false); }
  };
  const show = async share => {
    setFailure(null);
    try { setCreated(await api.revealShare(conversation.id, share.id)); }
    catch (problem) { setFailure(problem); }
  };
  const revoke = async share => {
    try { await api.revokeShare(conversation.id, share.id); await reload(); }
    catch (problem) { setFailure(problem); }
  };
  return <Modal title="share.title" wide onClose={onClose}>
    <p>{t("share.description", { title: conversation.title })}</p>
    {created ? <section className="share-created" aria-live="polite">
      <h3>{t("share.created")}</h3>
      <div className="token-reveal"><code>{shareUrl(created.path)}</code><CopyButton value={shareUrl(created.path)} label="share.copy" /></div>
      <p className="muted">{t("share.created.help", { date: formatDate(created.share.expiresUtc) })}</p>
      <Button label="share.another" onClick={() => setCreated(null)} />
    </section> : <section className="share-form">
      <div className="form-grid">
        <Field label="share.expires">{props => <select {...props} value={hours} onChange={event => setHours(Number(event.target.value))}>{shareLifetimes.map(([key, value]) => <option key={key} value={value}>{t("share.lifetime." + key)}</option>)}</select>}</Field>
      </div>
      <label className="checkbox-label" title={t("share.tools.help")}><input type="checkbox" checked={tools} onChange={event => setTools(event.target.checked)} />{t("share.tools")}</label>
      <Button label="share.create" className="primary" disabled={busy} onClick={create} />
    </section>}
    {failure && <ErrorPanel error={failure} />}
    <h3>{t("share.existing")}</h3>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && data.length === 0 && <div className="empty-state">{t("share.empty")}</div>}
    {data && data.length > 0 && <div className="table-scroll"><table>
      <thead><tr><th scope="col">{t("share.link")}</th><th scope="col">{t("share.status")}</th><th scope="col">{t("share.expires")}</th><th scope="col">{t("share.views")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{data.map(share => <ObjectRow key={share.id} inspect onOpen={() => setViewing(share)}>
        <td><code>{"…" + share.tokenLast4}</code>{share.includeToolCalls && <> <span className="badge">{t("share.withTools")}</span></>}<div className="muted">{t("share.createdOn", { date: formatDate(share.createdUtc) })}</div></td>
        <td><span className={share.active ? "badge success" : "badge"}>{t(share.revokedUtc ? "share.revoked" : share.active ? "share.active" : "share.expired")}</span></td>
        <td><time>{formatDate(share.expiresUtc)}</time></td>
        <td>{share.viewCount}{share.lastViewedUtc && <div className="muted">{t("share.lastViewed", { date: formatDate(share.lastViewedUtc) })}</div>}</td>
        <td className="actions-cell"><ActionMenu context={"…" + share.tokenLast4} items={[{ label: "share.show", help: "share.show.help", disabled: !share.active, onClick: () => show(share) }, { label: "share.revoke", danger: true, disabled: !share.active, onClick: () => revoke(share) }]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {viewing && <DetailsDialog title="share.details" heading={"…" + viewing.tokenLast4} record={viewing} prefix="share" onClose={() => setViewing(null)}
      actions={viewing.active && <><Button label="share.show" onClick={async () => { await show(viewing); setViewing(null); }} /><Button label="share.revoke" className="danger" onClick={async () => { await revoke(viewing); setViewing(null); }} /></>} />}
  </Modal>;
}
