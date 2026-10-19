import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal, Pagination } from "../components/ui.jsx";

export default function Roles() {
  const { api, user } = useOutletContext();
  const { t } = useTranslation();
  const [filter, setFilter] = useState({ search: "", pageNumber: 1, pageSize: 25 });
  const [draft, setDraft] = useState(null);
  const [failure, setFailure] = useState(null);
  const [busy, setBusy] = useState(false);
  const { data, error, loading, reload } = useData(JSON.stringify(filter), () =>
    api.request("/v1.0/api/roles?" + new URLSearchParams(filter)),
  );
  function edit(entry, clone = false) {
    setFailure(null);
    setDraft({
      id: clone ? null : entry?.id,
      name: clone ? t("roles.copyName", { name: entry.name }) : entry?.name || "",
      active: clone ? true : entry?.active ?? true,
      expectedRevision: clone ? 0 : entry?.revision || 0,
      cloneRoleId: clone ? entry.id : "",
      cloneName: clone ? entry.name : "",
    });
  }
  async function save(event) {
    event.preventDefault(); setBusy(true); setFailure(null);
    try {
      const { id, cloneName: _source, ...body } = draft;
      await api.request("/v1.0/api/roles" + (id ? "/" + encodeURIComponent(id) : ""), {
        method: id ? "PUT" : "POST", body,
      });
      setDraft(null); reload();
    } catch (error) { setFailure(error); }
    finally { setBusy(false); }
  }
  if ((!user.isAdmin && !user.isTenantAdmin) || error?.status === 403)
    return <ErrorPanel error={error || { message: t("roles.forbidden") }} />;
  return (
    <>
      <div className="page-heading">
        <div><h1 title={t("roles.description")}>{t("roles.title")}</h1><p>{t("roles.description")}</p></div>
        <div className="actions">
          <Button label="common.refresh" icon="refresh" onClick={reload} />
          <Button label="roles.create" icon="plus" onClick={() => edit(null)} />
        </div>
      </div>
      <div className="filters">
        <Field label="roles.search">{(props) => (
          <input {...props} type="search" maxLength={200} value={filter.search}
            onChange={(event) => setFilter({ ...filter, search: event.target.value, pageNumber: 1 })} />
        )}</Field>
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      {loading && <p role="status">{t("common.loading")}</p>}
      {data && <>
        <Pagination page={data} onChange={(values) => setFilter({ ...filter, ...values })} />
        <div className="table-scroll"><table>
          <thead><tr>{["roles.name", "roles.kind", "users.active", "common.actions"].map((key) => (
            <th key={key} scope="col" title={t(key)}>{t(key)}</th>
          ))}</tr></thead>
          <tbody>{data.items.map((entry) => (
            <tr key={entry.id}>
              <td title={entry.id}>{entry.name}</td>
              <td title={t("roles.kind.help")}>{t(entry.builtin ? "roles.builtin" : "roles.custom")}</td>
              <td title={t("roles.active.help")}>{t(entry.active ? "users.enabled" : "users.disabled")}</td>
              <td><div className="actions">
                {entry.protected ? <span title={t("roles.protected.help")}>{t("users.protected")}</span> :
                  <Button label="roles.edit" onClick={() => edit(entry)} />}
                <Button label="roles.clone" onClick={() => edit(entry, true)} />
              </div></td>
            </tr>
          ))}</tbody>
        </table></div>
        {data.items.length === 0 && <p className="empty-state">{t("roles.empty")}</p>}
      </>}
      {draft && <Modal title={draft.id ? "roles.edit" : "roles.create"} onClose={() => !busy && setDraft(null)}>
        <form onSubmit={save}>
          {draft.cloneRoleId && <p title={t("roles.clone.help")}>{t("roles.source", { name: draft.cloneName })}</p>}
          <Field label="roles.name">{(props) => (
            <input {...props} required maxLength={200} value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })} />
          )}</Field>
          <Field label="roles.active">{(props) => (
            <input {...props} type="checkbox" checked={draft.active}
              onChange={(event) => setDraft({ ...draft, active: event.target.checked })} />
          )}</Field>
          {failure && <ErrorPanel error={failure} />}
          <div className="actions">
            <Button label="common.save" type="submit" disabled={busy} />
            <Button label="common.cancel" type="button" disabled={busy} onClick={() => setDraft(null)} />
          </div>
        </form>
      </Modal>}
    </>
  );
}
