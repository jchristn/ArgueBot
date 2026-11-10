import { useCallback, useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import ActionMenu from "../components/ActionMenu.jsx";
import CopyableId from "../components/CopyableId.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { BulkDeleteButton, BulkDeleteDialog, SelectCell, SelectHeader, useSelection } from "../components/BulkDelete.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";

/** Tenants: system administrators create, rename, disable and delete them, and open a tenant's users. */
export default function Tenants() {
  const { api, session } = useOutletContext();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data, error, reload } = useData("tenants", () => api.tenants());
  const [editing, setEditing] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [failure, setFailure] = useState(null);
  const [bulk, setBulk] = useState(false);
  const deletable = useCallback(tenant => tenant.protected ? "bulk.blocked.protected" : tenant.id === session?.tenant?.id ? "bulk.blocked.ownTenant" : "", [session?.tenant?.id]);
  const selection = useSelection(data, deletable);
  return <>
    <div className="page-heading"><div><h1>{t("tenants.title")}</h1><p>{t("tenants.description")}</p></div></div>
    <div className="tab-toolbar"><div className="toolbar-actions">
      <BulkDeleteButton selection={selection} onClick={() => setBulk(true)} />
      <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
      <Button label="tenants.add" icon="plus" className="primary" onClick={() => { setFailure(null); setEditing({ name: "", active: true }); }} />
    </div></div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && <div className="table-scroll"><table>
      <thead><tr><SelectHeader selection={selection} /><th scope="col">{t("tenants.name")}</th><th scope="col">{t("tenants.id")}</th><th scope="col">{t("tenants.status")}</th><th scope="col">{t("tenants.updated")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{data.map(tenant => <ObjectRow key={tenant.id} onOpen={() => { setFailure(null); setEditing({ ...tenant }); }}>
        <SelectCell selection={selection} item={tenant} name={tenant.name} />
        <td><strong>{tenant.name}</strong>{tenant.id === session.tenant.id && <> <span className="badge">{t("tenants.yours")}</span></>}{tenant.protected && <> <span className="badge" title={t("tenants.protected.help")}>{t("tenants.protected")}</span></>}</td>
        <td><CopyableId value={tenant.id} /></td>
        <td><span className={tenant.active ? "badge success" : "badge"}>{t(tenant.active ? "tenants.active" : "tenants.disabled")}</span></td>
        <td><time>{formatDate(tenant.updatedUtc)}</time></td>
        <td className="actions-cell"><ActionMenu context={tenant.name} items={[
          { label: "tenants.users", onClick: () => navigate("/dashboard/users" + (tenant.id === session.tenant.id ? "" : "?tenant=" + encodeURIComponent(tenant.id))) },
          { label: "common.edit", onClick: () => { setFailure(null); setEditing({ ...tenant }); } },
          { label: "common.delete", danger: true, hidden: tenant.protected || tenant.id === session.tenant.id, onClick: () => { setFailure(null); setDeleting({ tenant, confirm: "" }); } },
        ]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {editing && <Modal title={editing.id ? "tenants.edit" : "tenants.add"} onClose={() => setEditing(null)}>
      <form onSubmit={async event => { event.preventDefault(); try { await api.saveTenant({ ...editing, name: editing.name.trim() }); setEditing(null); await reload(); } catch (problem) { setFailure(problem); } }}>
        <Field label="tenants.name">{props => <input {...props} data-autofocus required maxLength={128} value={editing.name} onChange={event => setEditing({ ...editing, name: event.target.value })} />}</Field>
        <label className="checkbox-label" title={t("tenants.active.help")}><input type="checkbox" checked={editing.active} disabled={editing.protected} onChange={event => setEditing({ ...editing, active: event.target.checked })} />{t("tenants.activeLabel")}</label>
        {!editing.id && <p className="muted">{t("tenants.add.next")}</p>}
        {failure && <ErrorPanel error={failure} />}
        <footer className="dialog-actions"><Button label="common.cancel" type="button" onClick={() => setEditing(null)} /><Button label={editing.id ? "common.save" : "common.create"} type="submit" className="primary" disabled={!editing.name.trim()} /></footer>
      </form>
    </Modal>}
    {bulk && <BulkDeleteDialog items={selection.items()} nameOf={tenant => tenant.name} remove={tenant => api.deleteTenant(tenant.id, tenant.name)} confirmWord
      onClose={() => setBulk(false)} onDone={failed => { setBulk(false); selection.keep(failed); reload(); }} />}
    {deleting && <Modal title="tenants.delete.title" onClose={() => setDeleting(null)}>
      <p>{t("tenants.delete.confirm", { name: deleting.tenant.name })}</p>
      <Field label="tenants.delete.type">{props => <input {...props} data-autofocus autoComplete="off" value={deleting.confirm} onChange={event => setDeleting({ ...deleting, confirm: event.target.value })} />}</Field>
      {failure && <ErrorPanel error={failure} />}
      <footer className="dialog-actions"><Button label="common.cancel" onClick={() => setDeleting(null)} /><Button label="common.delete" className="danger" disabled={deleting.confirm !== deleting.tenant.name} onClick={async () => { try { await api.deleteTenant(deleting.tenant.id, deleting.confirm); setDeleting(null); await reload(); } catch (problem) { setFailure(problem); } }} /></footer>
    </Modal>}
  </>;
}
