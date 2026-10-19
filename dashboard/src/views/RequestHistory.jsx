import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Modal, Pagination } from "../components/ui.jsx";
import ActivityChart from "../components/ActivityChart.jsx";
import { formatDate } from "../i18n/index.js";
function Inspector({ record, onClose }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  return (
    <Modal title="history.inspect" onClose={onClose} wide>
      <div className="inspector-summary">
        <span className={"badge " + (record.status >= 400 ? "failure" : "")}>
          {record.status}
        </span>
        <code>
          {record.method} {record.path}
        </code>
        <span>{record.durationMs} ms</span>
      </div>
      <dl className="server-details">
        <dt>{t("history.time")}</dt>
        <dd>{formatDate(record.createdUtc)}</dd>
        <dt>{t("history.principal")}</dt>
        <dd>{record.principalName}</dd>
        <dt>{t("history.requestId")}</dt>
        <dd>
          <code>{record.correlationId}</code>
        </dd>
        <dt>{t("history.traceId")}</dt>
        <dd>
          <code>{record.traceId || t("common.none")}</code>
        </dd>
      </dl>
      {[false, true].map((response) => (
        <section key={String(response)} className="inspector-section">
          <h3>{t(response ? "history.response" : "history.request")}</h3>
          <table>
            <thead>
              <tr>
                <th scope="col">{t("history.header")}</th>
                <th scope="col">{t("history.value")}</th>
              </tr>
            </thead>
            <tbody>
              {record.headers
                .filter((header) => header.response === response)
                .map((header) => (
                  <tr key={header.name}>
                    <td>{header.name}</td>
                    <td>
                      <code>{header.value}</code>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
          <p className="muted">
            {(response ? record.responseTruncated : record.requestTruncated) &&
              t("history.truncated")}
          </p>
          <pre>
            {(response ? record.rawResponseBody : record.rawRequestBody) ||
              t("history.noBody")}
          </pre>
        </section>
      ))}
      <footer>
        <Button
          label={copied ? "common.copied" : "common.copy"}
          help="history.copy.help"
          onClick={async () => {
            await navigator.clipboard.writeText(
              JSON.stringify(record, null, 2),
            );
            setCopied(true);
          }}
        />
        <Button label="common.close" help="common.close" onClick={onClose} />
      </footer>
    </Modal>
  );
}
export default function RequestHistory() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const [filter, setFilter] = useState({
    search: "",
    method: "",
    status: "",
    pageNumber: 1,
    pageSize: 25,
  });
  const [detail, setDetail] = useState(null);
  const [deleting, setDeleting] = useState(null);
  const { data, error, reload } = useData(JSON.stringify(filter), () =>
    api.history(filter),
  );
  const range = useState(() => ({
    from: new Date(Date.now() - 24 * 3600000).toISOString(),
    to: new Date().toISOString(),
  }))[0];
  const chart = useData("history-chart", () =>
    api.historySummary(range.from, range.to),
  );
  const update = (changes) =>
    setFilter((current) => ({ ...current, ...changes }));
  const inspect = async (id) => {
    try {
      setDetail(await api.historyDetail(id));
    } catch (failure) {
      notify(failure);
    }
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("history.title")}</h1>
          <p>{t("history.description")}</p>
        </div>
        <Button
          label="common.refresh"
          icon="refresh"
          iconOnly
          onClick={() => {
            reload();
            chart.reload();
          }}
        />
      </div>
      {chart.data && <ActivityChart buckets={chart.data} />}
      <div className="filters">
        <input
          type="search"
          aria-label={t("history.path")}
          title={t("history.path.help")}
          placeholder={t("history.path")}
          value={filter.search}
          onChange={(event) =>
            update({ search: event.target.value, pageNumber: 1 })
          }
        />
        <select
          aria-label={t("history.method")}
          title={t("history.method.help")}
          value={filter.method}
          onChange={(event) =>
            update({ method: event.target.value, pageNumber: 1 })
          }
        >
          <option value="">{t("history.allMethods")}</option>
          {["GET", "POST", "PUT", "DELETE", "OPTIONS"].map((method) => (
            <option key={method}>{method}</option>
          ))}
        </select>
        <input
          type="number"
          min="100"
          max="599"
          aria-label={t("history.status")}
          title={t("history.status.help")}
          placeholder={t("history.status")}
          value={filter.status}
          onChange={(event) =>
            update({ status: event.target.value, pageNumber: 1 })
          }
        />
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}{" "}
      {data && (
        <>
          <Pagination page={data} onChange={update} />
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  {[
                    "history.time",
                    "history.method",
                    "history.path",
                    "history.status",
                    "history.duration",
                    "common.actions",
                  ].map((key) => (
                    <th scope="col" key={key}>
                      {t(key)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.items.map((record) => (
                  <tr key={record.id}>
                    <td>
                      <time>{formatDate(record.createdUtc)}</time>
                    </td>
                    <td>
                      <code>{record.method}</code>
                    </td>
                    <td>
                      <code>{record.path}</code>
                    </td>
                    <td>
                      <span
                        className={
                          "badge " + (record.status >= 400 ? "failure" : "")
                        }
                      >
                        {record.status}
                      </span>
                    </td>
                    <td>{record.durationMs} ms</td>
                    <td>
                      <div className="row-actions">
                        <Button
                          label="history.inspect"
                          help="history.inspect.help"
                          onClick={() => inspect(record.id)}
                        />
                        <Button
                          label="common.delete"
                          help="history.delete.help"
                          className="danger"
                          onClick={() => setDeleting(record.id)}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {data.items.length === 0 && (
            <div className="empty-state">{t("history.empty")}</div>
          )}
        </>
      )}
      {detail && <Inspector record={detail} onClose={() => setDetail(null)} />}{" "}
      {deleting && (
        <Modal title="common.delete" onClose={() => setDeleting(null)}>
          <p>{t("history.delete.confirm")}</p>
          <footer>
            <Button
              label="common.cancel"
              help="common.cancel"
              onClick={() => setDeleting(null)}
            />
            <Button
              label="common.delete"
              help="history.delete.help"
              className="danger"
              onClick={async () => {
                try {
                  await api.deleteHistory(deleting);
                  setDeleting(null);
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
