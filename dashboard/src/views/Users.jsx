import { useState } from "react";
import { useOutletContext, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import ActionMenu from "../components/ActionMenu.jsx";
import AuthCredentials from "../components/AuthCredentials.jsx";
import AuthSessions from "../components/AuthSessions.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";

/** Role label key for a user. */
export function roleKey(user) {
  return user.isAdmin ? "users.role.system" : user.isTenantAdmin ? "users.role.tenant" : "users.role.member";
}

/** Users of the caller's tenant (tenant administrators), or of any tenant (system administrators). */
export default function Users() {
  const { api, notify, session } = useOutletContext();
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const systemAdmin = !!session?.user?.isAdmin;
  const tenantId = systemAdmin && params.get("tenant") && params.get("tenant") !== session.tenant.id ? params.get("tenant") : "";
  const tenants = useData("users-tenants:" + systemAdmin, () => (systemAdmin ? api.tenants() : Promise.resolve([])));
  const { data, error, reload } = useData("users:" + tenantId, () => api.users(tenantId));
  const [editing, setEditing] = useState(null);
  const [editError, setEditError] = useState(null);
  const [resetting, setResetting] = useState(null);
  const [managing, setManaging] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [failure, setFailure] = useState(null);
  const tenantName = tenantId ? tenants.data?.find(item => item.id === tenantId)?.name || tenantId : session?.tenant?.name;
  const save = async event => {
    event.preventDefault(); setEditError(null);
    try { await api.saveUser({ ...editing, email: editing.email.trim() }, tenantId); setEditing(null); await reload(); }
    catch (problem) { setEditError(problem); }
  };
  return <>
    <div className="page-heading"><div><h1>{t("users.title")}</h1><p>{t("users.description", { tenant: tenantName || "" })}</p></div></div>
    <div className="tab-toolbar">
      {systemAdmin && tenants.data && <Field label="users.tenant">{props => <select {...props} value={tenantId || session.tenant.id} onChange={event => setParams(event.target.value === session.tenant.id ? {} : { tenant: event.target.value })}>{tenants.data.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>}</Field>}
      <div className="toolbar-actions">
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
        <Button label="users.add" icon="plus" className="primary" onClick={() => { setEditError(null); setEditing({ firstName: "", lastName: "", email: "", password: "", isTenantAdmin: false, isAdmin: false, active: true }); }} />
      </div>
    </div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && <div className="table-scroll"><table>
      <thead><tr><th scope="col">{t("users.name")}</th><th scope="col">{t("users.email")}</th><th scope="col">{t("users.role")}</th><th scope="col">{t("users.status")}</th><th scope="col">{t("users.updated")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{data.map(user => <ObjectRow key={user.id} onOpen={() => { setEditError(null); setEditing({ ...user }); }}>
        <td><strong>{[user.firstName, user.lastName].filter(Boolean).join(" ") || user.email}</strong>{user.id === session.user.id && !tenantId && <> <span className="badge">{t("users.you")}</span></>}{user.protected && <> <span className="badge" title={t("users.protected.help")}>{t("users.protected")}</span></>}</td>
        <td>{user.email}</td>
        <td>{t(roleKey(user))}</td>
        <td><span className={user.active ? "badge success" : "badge"}>{t(user.active ? "users.active" : "users.disabled")}</span></td>
        <td><time>{formatDate(user.updatedUtc)}</time></td>
        <td className="actions-cell"><ActionMenu context={user.email} items={[
          { label: "common.edit", onClick: () => { setEditError(null); setEditing({ ...user }); } },
          { label: "users.resetPassword", onClick: () => { setFailure(null); setResetting({ user, password: "" }); } },
          { label: "users.manageAccess", help: "users.manageAccess.help", onClick: () => setManaging(user) },
          { label: "common.delete", danger: true, hidden: user.protected || user.id === session.user.id, onClick: () => { setFailure(null); setDeleting(user); } },
        ]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {editing && <Modal title={editing.id ? "users.edit" : "users.add"} onClose={() => setEditing(null)}>
      <form onSubmit={save}>
        <div className="form-grid">
          <Field label="account.firstName">{props => <input {...props} data-autofocus maxLength={128} value={editing.firstName} onChange={event => setEditing({ ...editing, firstName: event.target.value })} />}</Field>
          <Field label="account.lastName">{props => <input {...props} maxLength={128} value={editing.lastName} onChange={event => setEditing({ ...editing, lastName: event.target.value })} />}</Field>
        </div>
        <Field label="account.email">{props => <input {...props} type="email" required maxLength={256} value={editing.email} onChange={event => setEditing({ ...editing, email: event.target.value })} />}</Field>
        {!editing.id && <Field label="users.password">{props => <input {...props} type="password" autoComplete="new-password" required minLength={8} value={editing.password} onChange={event => setEditing({ ...editing, password: event.target.value })} />}</Field>}
        <label className="checkbox-label" title={t("users.tenantAdmin.help")}><input type="checkbox" checked={editing.isTenantAdmin || editing.isAdmin} disabled={editing.isAdmin || editing.protected} onChange={event => setEditing({ ...editing, isTenantAdmin: event.target.checked })} />{t("users.tenantAdmin")}</label>
        {systemAdmin && <label className="checkbox-label" title={t("users.systemAdmin.help")}><input type="checkbox" checked={editing.isAdmin} disabled={editing.protected} onChange={event => setEditing({ ...editing, isAdmin: event.target.checked, isTenantAdmin: event.target.checked || editing.isTenantAdmin })} />{t("users.systemAdmin")}</label>}
        <label className="checkbox-label" title={t("users.active.help")}><input type="checkbox" checked={editing.active} disabled={editing.protected} onChange={event => setEditing({ ...editing, active: event.target.checked })} />{t("users.activeLabel")}</label>
        {editError && <ErrorPanel error={editError} />}
        <footer className="dialog-actions"><Button label="common.cancel" type="button" onClick={() => setEditing(null)} /><Button label={editing.id ? "common.save" : "common.create"} type="submit" className="primary" disabled={!editing.email.trim() || (!editing.id && editing.password.length < 8)} /></footer>
      </form>
    </Modal>}
    {resetting && <Modal title="users.resetPassword" onClose={() => setResetting(null)}>
      <form onSubmit={async event => { event.preventDefault(); try { await api.resetPassword(resetting.user.id, resetting.password, tenantId); setResetting(null); notify({ message: t("users.resetPassword.done", { email: resetting.user.email }) }); } catch (problem) { setFailure(problem); } }}>
        <p>{t("users.resetPassword.help", { email: resetting.user.email })}</p>
        <Field label="account.password.new">{props => <input {...props} type="password" autoComplete="new-password" data-autofocus required minLength={8} value={resetting.password} onChange={event => setResetting({ ...resetting, password: event.target.value })} />}</Field>
        {failure && <ErrorPanel error={failure} />}
        <footer className="dialog-actions"><Button label="common.cancel" type="button" onClick={() => setResetting(null)} /><Button label="users.resetPassword" type="submit" className="primary" disabled={resetting.password.length < 8} /></footer>
      </form>
    </Modal>}
    {managing && <Modal title="users.manageAccess" wide onClose={() => setManaging(null)}>
      <p>{t("users.manageAccess.for", { email: managing.email })}</p>
      <AuthCredentials api={api} notify={notify} userId={managing.id} tenantId={tenantId} />
      <AuthSessions api={api} notify={notify} userId={managing.id} tenantId={tenantId} />
    </Modal>}
    {deleting && <Modal title="users.delete.title" onClose={() => setDeleting(null)}>
      <p>{t("users.delete.confirm", { email: deleting.email })}</p>
      {failure && <ErrorPanel error={failure} />}
      <footer className="dialog-actions"><Button label="common.cancel" onClick={() => setDeleting(null)} /><Button label="common.delete" className="danger" onClick={async () => { try { await api.deleteUser(deleting.id, tenantId); setDeleting(null); await reload(); } catch (problem) { setFailure(problem); } }} /></footer>
    </Modal>}
  </>;
}
