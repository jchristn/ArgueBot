import ActionMenu from "../components/ActionMenu.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { BulkDeleteButton, BulkDeleteDialog, SelectCell, SelectHeader, useSelection } from "../components/BulkDelete.jsx";
import PromptEditor from "../components/PromptEditor.jsx";
import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";

const deletable = prompt => prompt.factory ? "bulk.blocked.factory" : "";
export const promptPurposes = ["question", "response", "synthesis", "ratification", "compaction"];

/** Prompt library: factory defaults (read-only) and workspace prompts, with duplicate, edit and delete. */
export default function Prompts() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const [purpose, setPurpose] = useState("");
  const [editing, setEditing] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const [deleteError, setDeleteError] = useState(null);
  const [bulk, setBulk] = useState(false);
  const { data, error, reload } = useData("prompts", async () => {
    const [prompts, parameters] = await Promise.all([api.prompts(), api.promptParameters()]);
    return { prompts, parameters };
  });
  const rows = (data?.prompts || []).filter(item => !purpose || item.purpose === purpose);
  const selection = useSelection(rows, deletable);
  const duplicate = async prompt => {
    try { const copy = await api.duplicatePrompt(prompt.id); await reload(); setEditing(copy); }
    catch (failure) { notify(failure); }
  };
  return <>
    <div className="page-heading">
      <div>
        <h1>{t("prompts.title")}</h1>
        <p>{t("prompts.description")}</p>
      </div>
    </div>
    <div className="tab-toolbar">
      <Field label="prompts.purposeFilter">{props => <select {...props} value={purpose} onChange={event => setPurpose(event.target.value)}><option value="">{t("prompts.allPurposes")}</option>{promptPurposes.map(item => <option key={item} value={item}>{t(`prompts.purpose.${item}`)}</option>)}</select>}</Field>
      <div className="toolbar-actions">
        <BulkDeleteButton selection={selection} onClick={() => setBulk(true)} />
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
        <Button label="prompts.add" icon="plus" className="primary" disabled={!data} onClick={() => setEditing({ name: "", purpose: purpose || "question", description: "", body: "", revision: 0 })} />
      </div>
    </div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && <div className="table-scroll"><table>
      <thead><tr><SelectHeader selection={selection} /><th scope="col">{t("prompts.name")}</th><th scope="col">{t("prompts.purposeColumn")}</th><th scope="col">{t("prompts.kind")}</th><th scope="col">{t("prompts.updated")}</th><th scope="col" className="actions-cell">{t("common.actions")}</th></tr></thead>
      <tbody>{rows.map(prompt => <ObjectRow key={prompt.id} onOpen={() => setEditing(prompt)}>
        <SelectCell selection={selection} item={prompt} name={prompt.name} />
        <td><strong>{prompt.name}</strong>{prompt.description && <div className="muted prompt-description">{prompt.description}</div>}</td>
        <td>{t(`prompts.purpose.${prompt.purpose}`)}</td>
        <td><span className={prompt.factory ? "badge" : "badge success"} title={t(prompt.factory ? "prompts.factory.help" : "prompts.custom.help")}>{t(prompt.factory ? "prompts.factory" : "prompts.custom")}</span></td>
        <td><time>{formatDate(prompt.updatedUtc)}</time></td>
        <td className="actions-cell"><ActionMenu context={prompt.name} items={[
          { label: prompt.factory ? "prompts.view" : "common.edit", onClick: () => setEditing(prompt) },
          { label: "prompts.duplicate", help: "prompts.duplicate.help", onClick: () => duplicate(prompt) },
          { label: "common.delete", danger: true, hidden: prompt.factory, onClick: () => { setDeleteError(null); setDeleting(prompt); } },
        ]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {data && rows.length === 0 && <div className="empty-state">{t("prompts.empty")}</div>}
    {editing && data && <PromptEditor api={api} prompt={editing} parameters={data.parameters} onDuplicate={async () => { const source = editing; setEditing(null); await duplicate(source); }} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await reload(); }} />}
    {bulk && <BulkDeleteDialog items={selection.items()} nameOf={prompt => prompt.name} remove={prompt => api.deletePrompt(prompt.id, prompt.revision)}
      onClose={() => setBulk(false)} onDone={failed => { setBulk(false); selection.keep(failed); reload(); }} />}
    {deleting && <Modal title="prompts.delete.title" onClose={() => setDeleting(null)}>
      <p>{t("prompts.delete.confirm", { name: deleting.name })}</p>
      {deleteError && <ErrorPanel error={deleteError} />}
      <footer className="dialog-actions">
        <Button label="common.cancel" onClick={() => setDeleting(null)} />
        <Button label="common.delete" className="danger" onClick={async () => { try { await api.deletePrompt(deleting.id, deleting.revision); setDeleting(null); await reload(); } catch (failure) { setDeleteError(failure); } }} />
      </footer>
    </Modal>}
  </>;
}
