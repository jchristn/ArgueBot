import { useEffect, useRef, useState } from "react";
import { useOutletContext, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import ActionMenu from "../components/ActionMenu.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { BulkDeleteButton, BulkDeleteDialog, SelectCell, SelectHeader, useSelection } from "../components/BulkDelete.jsx";
import ResourceEditor from "../components/ResourceEditor.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";
import { formatSize, readImportFiles } from "../utils/resources.js";

/** Resource library: named markdown documents agents can read through tools, with search, tags, import and an editor with history. */
export default function Resources() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState("");
  const [tag, setTag] = useState("");
  const [editing, setEditing] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [deleteError, setDeleteError] = useState(null);
  const [importing, setImporting] = useState(null);
  const [bulk, setBulk] = useState(false);
  const picker = useRef();
  const { data, error, reload } = useData("resources", () => api.resources());
  const items = data?.items || [];
  const tags = [...new Set(items.flatMap(item => item.tags))].sort();
  const needle = search.trim().toLowerCase();
  const rows = items.filter(item => (!tag || item.tags.includes(tag)) && (!needle || [item.name, item.title, item.description].some(text => text.toLowerCase().includes(needle))));
  const selection = useSelection(rows);
  // Links from transcript tool activity open a resource by name, optionally at a revision.
  const linkedName = params.get("name") || "";
  const linkedRevision = Number(params.get("revision") || 0);
  useEffect(() => {
    if (!linkedName) return;
    api.resourceByName(linkedName).then(resource => setEditing({ resource, revision: linkedRevision })).catch(notify);
  }, [api, linkedName, linkedRevision, notify]);
  const close = () => { setEditing(null); if (linkedName) setParams({}, { replace: true }); };
  const importFiles = async (files, overwrite = false) => {
    if (!files?.length) return;
    try {
      const result = await api.importResources(await readImportFiles(files), overwrite);
      setImporting(null); await reload();
      notify({ message: t("resources.imported", { created: result.created, updated: result.updated }) });
    } catch (failure) {
      if (failure.code === "resource_exists") setImporting({ files, error: failure });
      else notify(failure);
    } finally { if (picker.current) picker.current.value = ""; }
  };
  return <>
    <div className="page-heading">
      <div>
        <h1>{t("resources.title")}</h1>
        <p>{t("resources.description")}</p>
      </div>
    </div>
    <div className="tab-toolbar" onDragOver={event => event.preventDefault()} onDrop={event => { event.preventDefault(); importFiles(event.dataTransfer.files); }}>
      <Field label="resources.search">{props => <input {...props} type="search" value={search} onChange={event => setSearch(event.target.value)} />}</Field>
      <Field label="resources.tagFilter">{props => <select {...props} value={tag} onChange={event => setTag(event.target.value)}><option value="">{t("resources.allTags")}</option>{tags.map(item => <option key={item} value={item}>{item}</option>)}</select>}</Field>
      <div className="toolbar-actions">
        <BulkDeleteButton selection={selection} onClick={() => setBulk(true)} />
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
        <input ref={picker} type="file" accept=".md,.markdown,text/markdown" multiple hidden data-testid="resource-import" onChange={event => importFiles(event.target.files)} />
        <Button label="resources.import" icon="download" onClick={() => picker.current?.click()} />
        <Button label="resources.add" icon="plus" className="primary" disabled={!data} onClick={() => setEditing({ resource: null, revision: 0 })} />
      </div>
    </div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && rows.length > 0 && <div className="table-scroll"><table>
      <thead><tr>
        <SelectHeader selection={selection} />
        <th scope="col">{t("resources.name")}</th><th scope="col">{t("resources.titleField")}</th><th scope="col">{t("resources.tagsColumn")}</th>
        <th scope="col">{t("resources.sizeColumn")}</th><th scope="col">{t("resources.updated")}</th><th scope="col">{t("resources.updatedBy")}</th>
        <th scope="col" className="actions-cell">{t("common.actions")}</th>
      </tr></thead>
      <tbody>{rows.map(item => <ObjectRow key={item.id} onOpen={() => setEditing({ resource: item, revision: 0 })}>
        <SelectCell selection={selection} item={item} name={item.name} />
        <td><code>{item.name}</code>{item.locked && <> <span className="badge" title={t("resources.locked.help")}>{t("resources.lockedBadge")}</span></>}{item.description && <div className="muted prompt-description">{item.description}</div>}</td>
        <td>{item.title}</td>
        <td>{item.tags.map(value => <span key={value} className="badge tag">{value}</span>)}</td>
        <td>{formatSize(item.bytes)}</td>
        <td><time>{formatDate(item.updatedUtc)}</time></td>
        <td>{item.updatedByKind === "agent" ? <span className="badge warning" title={t("resources.agent.help")}>{t("resources.agentNamed", { name: item.updatedByName })}</span> : item.updatedByName}</td>
        <td className="actions-cell"><ActionMenu context={item.name} items={[
          { label: "common.edit", onClick: () => setEditing({ resource: item, revision: 0 }) },
          { label: "resources.history", onClick: () => setEditing({ resource: item, revision: -1 }) },
          { label: "common.delete", danger: true, onClick: () => { setDeleteError(null); setDeleting(item); } },
        ]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {data && rows.length === 0 && <div className="empty-state">{t(items.length ? "resources.noMatches" : "resources.empty")}</div>}
    {editing && <ResourceEditor api={api} resource={editing.resource} initialRevision={editing.revision} onClose={close} onSaved={async () => { await reload(); }} />}
    {importing && <Modal title="resources.importConflict.title" onClose={() => setImporting(null)}>
      <p>{importing.error.message}</p>
      <p>{t("resources.importConflict.help")}</p>
      <footer className="dialog-actions">
        <Button label="common.cancel" onClick={() => setImporting(null)} />
        <Button label="resources.importOverwrite" className="danger" onClick={() => importFiles(importing.files, true)} />
      </footer>
    </Modal>}
    {bulk && <BulkDeleteDialog items={selection.items()} nameOf={item => item.name} remove={item => api.deleteResource(item.id, item.revision)}
      onClose={() => setBulk(false)} onDone={failed => { setBulk(false); selection.keep(failed); reload(); }} />}
    {deleting && <Modal title="resources.delete.title" onClose={() => setDeleting(null)}>
      <p>{t("resources.delete.confirm", { name: deleting.name })}</p>
      {deleteError && <ErrorPanel error={deleteError} />}
      <footer className="dialog-actions">
        <Button label="common.cancel" onClick={() => setDeleting(null)} />
        <Button label="common.delete" className="danger" onClick={async () => { try { await api.deleteResource(deleting.id, deleting.revision); setDeleting(null); await reload(); } catch (failure) { setDeleteError(failure); } }} />
      </footer>
    </Modal>}
  </>;
}
