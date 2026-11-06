import ShareDialog from "../components/ShareDialog.jsx";
import { randomId } from "../utils/random-id.js";
import ActionMenu from "../components/ActionMenu.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import CopyableId from "../components/CopyableId.jsx";
import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Modal, Pagination } from "../components/ui.jsx";
import ConversationForm from "../components/ConversationForm.jsx";
import { formatDate } from "../i18n/index.js";
import { savedPageSize } from "../utils/page-size.js";
const unfinished = run => Boolean(run) && !["completed", "cancelled", "failed"].includes(run.state);

/** Ask the service to stop a debate, then wait until it reports a finished state. */
async function stopAndWait(api, conversationId, run) {
  await api.action(conversationId, run.id, { action: "stop", idempotencyKey: randomId() });
  for (let attempt = 0; attempt < 60; attempt++) {
    const latest = (await api.runs(conversationId)).find(item => item.id === run.id);
    if (!unfinished(latest)) return true;
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  return false;
}

function StopDialog({ api, record, run, onClose, onDone }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [pending, setPending] = useState(false);
  const stop = async () => {
    setBusy(true); setError(null);
    try { if (await stopAndWait(api, record.id, run)) onDone(); else setPending(true); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  };
  return <Modal title="conversations.stop.title" onClose={() => !busy && onClose()}>
    <p>{t("conversations.stop.confirm", { title: record.title, state: t(`state.${run.state}`, { defaultValue: run.state }) })}</p>
    {error && <ErrorPanel error={error} />}
    {pending && <p className="notice" role="status">{t("conversations.stop.pending")}</p>}
    {busy && <p className="muted" role="status">{t("conversations.stop.stopping")}</p>}
    <footer className="dialog-actions">
      <Button label="common.cancel" disabled={busy} onClick={onClose} />
      <Button label="conversations.stop" className="danger" disabled={busy} onClick={stop} />
    </footer>
  </Modal>;
}

function DeleteDialog({ api, record, onClose, onDone }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [blocking, setBlocking] = useState(null);
  const remove = async () => {
    setBusy(true); setError(null);
    try { await api.deleteConversation(record.id); onDone(); }
    catch (failure) {
      if (failure.code === "active_run") {
        const run = (await api.runs(record.id).catch(() => [])).filter(unfinished).at(-1);
        if (run) { setBlocking(run); return; }
      }
      setError(failure);
    }
    finally { setBusy(false); }
  };
  const stopThenDelete = async () => {
    setBusy(true); setError(null);
    try {
      if (!await stopAndWait(api, record.id, blocking)) { setError(new Error(t("conversations.stop.pending"))); return; }
      await api.deleteConversation(record.id); onDone();
    } catch (failure) { setError(failure); }
    finally { setBusy(false); }
  };
  return <Modal title="common.delete" onClose={() => !busy && onClose()}>
    <p>{t("conversations.delete.confirm")}</p>
    {blocking && <p className="notice warning" role="alert">{t("conversations.delete.blocked", { state: t(`state.${blocking.state}`, { defaultValue: blocking.state }) })}</p>}
    {error && <ErrorPanel error={error} />}
    {busy && blocking && <p className="muted" role="status">{t("conversations.stop.stopping")}</p>}
    <footer className="dialog-actions">
      <Button label="common.cancel" help="common.cancel" disabled={busy} onClick={onClose} />
      {blocking
        ? <Button label="conversations.stopAndDelete" help="conversations.stopAndDelete.help" className="danger" disabled={busy} onClick={stopThenDelete} />
        : <Button label="common.delete" help="conversations.delete.help" className="danger" disabled={busy} onClick={remove} />}
    </footer>
  </Modal>;
}

export default function Conversations() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [filter, setFilter] = useState(() => ({
    search: "",
    archived: false,
    pageNumber: 1,
    pageSize: savedPageSize("conversations", 25),
  }));
  const [modal, setModal] = useState(null);
  const { data, error, reload: reloadList } = useData(JSON.stringify(filter), () =>
    api.conversations(filter),
  );
  const ids = (data?.items || []).map(record => record.id);
  // Latest run per visible row, loaded separately so the list does not wait on it.
  const { data: runs, reload: reloadRuns } = useData("conversation-runs:" + ids.join(","), async () => {
    const entries = await Promise.all(ids.map(async conversationId => { try { return [conversationId, (await api.runs(conversationId)).at(-1) || null]; } catch { return [conversationId, null]; } }));
    return Object.fromEntries(entries);
  });
  const reload = async () => { await reloadList(); await reloadRuns(); };
  const change = (update) => setFilter((value) => ({ ...value, ...update }));
  const update = async (record, changes) => {
    try {
      await api.updateConversation({ ...record, ...changes });
      await reload();
    } catch (failure) {
      notify(failure);
    }
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("conversations.title")}</h1>
          <p>{t("conversations.description")}</p>
        </div>
        <Button
          label="conversations.new"
          icon="plus"
          className="primary"
          onClick={() => setModal({ kind: "create" })}
        />
      </div>
      <div className="filters">
        <input
          type="search"
          value={filter.search}
          placeholder={t("common.search")}
          aria-label={t("common.search")}
          title={t("common.search.help")}
          onChange={(event) =>
            change({ search: event.target.value, pageNumber: 1 })
          }
        />
        <label title={t("conversations.archived.help")}>
          <input
            type="checkbox"
            title={t("conversations.archived.help")}
            checked={filter.archived}
            onChange={(event) =>
              change({ archived: event.target.checked, pageNumber: 1 })
            }
          />
          {t("conversations.archived")}
        </label>
        <Button
          label="common.refresh"
          icon="refresh"
          iconOnly
          onClick={reload}
        />
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}{" "}
      {data && (
        <>
          <Pagination page={data} table="conversations" onChange={change} />
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("conversations.titleColumn")}</th>
                  <th scope="col">{t("common.identifier")}</th>
                  <th scope="col">{t("conversations.updated")}</th>
                  <th scope="col" className="actions-cell">{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((record) => (
                  <ObjectRow key={record.id} open onOpen={() => navigate("/dashboard/conversations/" + record.id)}>
                    <td>
                      <strong>{record.pinned && "★ "}{record.title}</strong>
                    </td>
                    <td><CopyableId value={record.id} /></td>
                    <td>
                      <time>{formatDate(record.updatedUtc)}</time>
                    </td>
                    <td className="actions-cell">
                      <ActionMenu context={record.title} items={[
                        { label: "conversations.open", help: "conversations.open.help", onClick: () => navigate("/dashboard/conversations/" + record.id) },
                        { label: "reader.open", help: "reader.open.help", onClick: () => navigate("/dashboard/conversations/" + record.id + "/read") },
                        { label: "share.action", help: "share.action.help", onClick: () => setModal({ kind: "share", record }) },
                        { label: "conversations.renameAction", help: "conversations.rename.help", onClick: () => setModal({ kind: "rename", record }) },
                        { label: record.pinned ? "conversations.unpin" : "conversations.pin", help: "conversations.pin.help", onClick: () => update(record, { pinned: !record.pinned }) },
                        { label: record.archived ? "conversations.unarchive" : "conversations.archive", help: "conversations.archive.help", onClick: () => update(record, { archived: !record.archived }) },
                        { label: "conversations.stop", help: "conversations.stop.help", hidden: !unfinished(runs?.[record.id]), onClick: () => setModal({ kind: "stop", record, run: runs[record.id] }) },
                        { label: "common.inspect", help: "conversations.json.help", onClick: () => setModal({ kind: "json", record }) },
                        { label: "common.delete", help: "conversations.delete.help", danger: true, onClick: () => setModal({ kind: "delete", record }) },
                      ]} />
                    </td>
                  </ObjectRow>
                ))}
              </tbody>
            </table>
          </div>
          {data.items.length === 0 && (
            <div className="empty-state">{t("conversations.empty")}</div>
          )}
        </>
      )}
      {modal?.kind === "create" && (
        <ConversationForm
          api={api}
          onClose={() => setModal(null)}
          onSave={async (title, question, promptIds) => {
            try {
              const record = await api.createConversation(title, question, promptIds);
              setModal(null);
              navigate("/dashboard/conversations/" + record.id);
            } catch (failure) {
              notify(failure);
            }
          }}
        />
      )}{" "}
      {modal?.kind === "rename" && (
        <ConversationForm
          initial={modal.record.title}
          onClose={() => setModal(null)}
          onSave={async (title, question) => {
            await update(modal.record, { title });
            setModal(null);
          }}
        />
      )}{" "}
      {modal?.kind === "json" && (
        <Modal title="common.inspect" onClose={() => setModal(null)}>
          <pre>{JSON.stringify(modal.record, null, 2)}</pre>
        </Modal>
      )}{" "}
      {modal?.kind === "share" && <ShareDialog api={api} conversation={modal.record} onClose={() => setModal(null)} />}
      {modal?.kind === "delete" && <DeleteDialog api={api} record={modal.record} onClose={() => setModal(null)} onDone={() => { setModal(null); reload(); }} />}
      {modal?.kind === "stop" && <StopDialog api={api} record={modal.record} run={modal.run} onClose={() => setModal(null)} onDone={() => { setModal(null); reload(); }} />}
    </>
  );
}
