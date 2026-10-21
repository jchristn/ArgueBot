import ObjectRow from "../components/ObjectRow.jsx";
import RequestInspector from "../components/RequestInspector.jsx";
import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Modal, Pagination } from "../components/ui.jsx";
import ActivityChart from "../components/ActivityChart.jsx";
import { formatDate } from "../i18n/index.js";
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
  const [period, setPeriod] = useState({ name: "day", end: Date.now() });
  const periods = {
    hour: { minutes: 1, count: 60 },
    day: { minutes: 15, count: 96 },
    week: { minutes: 120, count: 84 },
    month: { minutes: 480, count: 90 },
  };
  const chosen = periods[period.name];
  const range = {
    fromUtc: new Date(period.end - chosen.minutes * chosen.count * 60000).toISOString(),
    toUtc: new Date(period.end).toISOString(),
  };
  const chart = useData(JSON.stringify(period), async () => ({
    buckets: await api.historySummary(range.fromUtc, range.toUtc, chosen.minutes),
    ...range,
    bucketMinutes: chosen.minutes,
    period: period.name,
  }));
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
            setPeriod(current => ({ ...current, end: Date.now() }));
          }}
        />
      </div>
      <div className="chart-controls">
        <label>{t("history.timeframe")}
          <select aria-label={t("history.timeframe")} value={period.name} onChange={event => setPeriod({ name: event.target.value, end: Date.now() })}>
            {Object.keys(periods).map(name => <option key={name} value={name}>{t("history.period." + name)}</option>)}
          </select>
        </label>
        <p className="muted">{t("history.bucket." + period.name)}</p>
      </div>
      {chart.error && <ErrorPanel error={chart.error} retry={chart.reload} />}
      {chart.data?.period === period.name && <ActivityChart key={chart.data.toUtc} {...chart.data} />}

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
                  <ObjectRow key={record.id} inspect onOpen={() => setDetail(record)}>
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
                  </ObjectRow>
                ))}
              </tbody>
            </table>
          </div>
          {data.items.length === 0 && (
            <div className="empty-state">{t("history.empty")}</div>
          )}
        </>
      )}
      {detail && <RequestInspector record={detail} onClose={() => setDetail(null)} />}{" "}
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
