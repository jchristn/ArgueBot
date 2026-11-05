import { useTranslation } from "react-i18next";
import ActionMenu from "./ActionMenu.jsx";
import { useData } from "./useData.js";
import { Button, ErrorPanel } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

/** Active sign-in sessions for the signed-in user, or for another user when an administrator passes userId. */
export default function AuthSessions({ api, notify, userId = "", tenantId = "" }) {
  const { t } = useTranslation();
  const own = !userId;
  const { data, error, reload } = useData("auth-sessions:" + tenantId + ":" + userId, () => api.sessions(userId, tenantId));
  const revoke = async (ids) => {
    try { for (const id of ids) await api.revokeSession(id, own, tenantId); await reload(); }
    catch (problem) { notify(problem); }
  };
  const others = (data || []).filter(item => !item.current).map(item => item.id);
  return <section className="page-section">
    <header><h2>{t("authSessions.title")}</h2>{own && <Button label="authSessions.signOutOthers" disabled={others.length === 0} onClick={() => revoke(others)} />}</header>
    <p className="muted">{t("authSessions.description")}</p>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && data.length === 0 && <div className="empty-state">{t("authSessions.empty")}</div>}
    {data && data.length > 0 && <div className="table-scroll"><table>
      <thead><tr><th scope="col">{t("authSessions.started")}</th><th scope="col">{t("authSessions.lastUsed")}</th><th scope="col">{t("authSessions.expires")}</th><th scope="col">{t("authSessions.address")}</th><th scope="col">{t("authSessions.client")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{data.map(item => <tr key={item.id}>
        <td><time>{formatDate(item.createdUtc)}</time>{item.current && <> <span className="badge success">{t("authSessions.current")}</span></>}</td>
        <td>{item.lastUsedUtc ? <time>{formatDate(item.lastUsedUtc)}</time> : ""}</td>
        <td><time>{formatDate(item.expiresUtc)}</time></td>
        <td><code>{item.sourceIp}</code></td>
        <td className="session-client" title={item.userAgent}>{item.userAgent}</td>
        <td className="actions-cell"><ActionMenu context={formatDate(item.createdUtc)} items={[{ label: item.current ? "authSessions.signOutThis" : "authSessions.revoke", disabled: item.current, help: item.current ? "authSessions.signOutThis.help" : undefined, onClick: () => revoke([item.id]) }]} /></td>
      </tr>)}</tbody>
    </table></div>}
  </section>;
}
