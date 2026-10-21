import ObjectRow from "../components/ObjectRow.jsx";
import CopyableId from "../components/CopyableId.jsx";
import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Modal, Pagination } from "../components/ui.jsx";
import ConversationForm from "../components/ConversationForm.jsx";
import { formatDate } from "../i18n/index.js";
export default function Conversations() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [filter, setFilter] = useState({
    search: "",
    archived: false,
    pageNumber: 1,
    pageSize: 25,
  });
  const [modal, setModal] = useState(null);
  const { data, error, reload } = useData(JSON.stringify(filter), () =>
    api.conversations(filter),
  );
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
          <Pagination page={data} onChange={change} />
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t("conversations.titleColumn")}</th>
                  <th scope="col">{t("common.identifier")}</th>
                  <th scope="col">{t("conversations.updated")}</th>
                  <th scope="col">{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((record) => (
                  <ObjectRow key={record.id} onOpen={() => setModal({ kind: "rename", record })}>
                    <td>
                      <strong>{record.pinned && "★ "}{record.title}</strong>
                    </td>
                    <td><CopyableId value={record.id} /></td>
                    <td>
                      <time>{formatDate(record.updatedUtc)}</time>
                    </td>
                    <td>
                      <div className="row-actions">
                        <Button label="conversations.open" onClick={() => navigate("/dashboard/conversations/" + record.id)} />
                        <Button
                          label="common.edit"
                          help="conversations.rename.help"
                          onClick={() => setModal({ kind: "rename", record })}
                        />
                        <Button
                          label={
                            record.pinned
                              ? "conversations.unpin"
                              : "conversations.pin"
                          }
                          help="conversations.pin.help"
                          onClick={() =>
                            update(record, { pinned: !record.pinned })
                          }
                        />
                        <Button
                          label={
                            record.archived
                              ? "conversations.unarchive"
                              : "conversations.archive"
                          }
                          help="conversations.archive.help"
                          onClick={() =>
                            update(record, { archived: !record.archived })
                          }
                        />
                        <Button
                          label="common.inspect"
                          help="conversations.json.help"
                          onClick={() => setModal({ kind: "json", record })}
                        />
                        <Button
                          label="common.delete"
                          help="conversations.delete.help"
                          className="danger"
                          onClick={() => setModal({ kind: "delete", record })}
                        />
                      </div>
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
          onClose={() => setModal(null)}
          onSave={async (title) => {
            try {
              const record = await api.createConversation(title);
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
          onSave={async (title) => {
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
      {modal?.kind === "delete" && (
        <Modal title="common.delete" onClose={() => setModal(null)}>
          <p>{t("conversations.delete.confirm")}</p>
          <footer>
            <Button
              label="common.cancel"
              help="common.cancel"
              onClick={() => setModal(null)}
            />
            <Button
              label="common.delete"
              help="conversations.delete.help"
              className="danger"
              onClick={async () => {
                try {
                  await api.deleteConversation(modal.record.id);
                  setModal(null);
                  reload();
                } catch (failure) {
                  notify(failure);
                }
              }}
            />
          </footer>
        </Modal>
      )}
    </>
  );
}
