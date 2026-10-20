import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal, Pagination } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";

export default function Audit() {
  const { api } = useOutletContext();
  const { t } = useTranslation();
  const [filter, setFilter] = useState({ operation: "", pageNumber: 1, pageSize: 25 });
  const [detail, setDetail] = useState(null);
  const { data, error, loading, reload } = useData(JSON.stringify(filter), () =>
    api.request("/v1.0/api/audit?" + new URLSearchParams(filter)),
  );
  const actor = (entry) => entry.userId || t(entry.authScheme === "SystemMigration" ? "audit.system" : "audit.anonymous");
  const update = (changes) => setFilter((current) => ({ ...current, ...changes }));
  return (
    <>
      <div className="page-heading">
        <div><h1 title={t("audit.description")}>{t("audit.title")}</h1><p>{t("audit.description")}</p></div>
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
      </div>
      <div className="filters">
        <Field label="audit.operation">
          {(props) => <input {...props} type="search" maxLength={100} value={filter.operation} onChange={(event) => update({ operation: event.target.value, pageNumber: 1 })} />}
        </Field>
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      {loading && <p role="status">{t("common.loading")}</p>}
      {data && <>
        <Pagination page={data} onChange={update} />
        <div className="table-scroll"><table>
          <thead><tr>{["audit.time", "audit.operation", "audit.outcome", "audit.actor", "common.actions"].map((key) => <th key={key} scope="col" title={t(key)}>{t(key)}</th>)}</tr></thead>
          <tbody>{data.items.map((entry) => <tr key={entry.id}>
            <td><time>{formatDate(entry.createdUtc)}</time></td>
            <td><code>{entry.operation}</code></td>
            <td><span className={"badge " + (entry.outcome === "denied" ? "failure" : "")} title={entry.errorCode || entry.outcome}>{entry.outcome}</span></td>
            <td><code title={entry.tenantId || t("audit.anonymous")}>{actor(entry)}</code></td>
            <td><Button label="audit.inspect" onClick={() => setDetail(entry)} /></td>
          </tr>)}</tbody>
        </table></div>
        {data.items.length === 0 && <p className="empty-state">{t("audit.empty")}</p>}
      </>}
      {detail && <Modal title="audit.inspect" onClose={() => setDetail(null)} wide>
        <dl className="server-details">
          {[["audit.id", detail.id], ["audit.time", formatDate(detail.createdUtc)], ["audit.operation", detail.operation], ["audit.outcome", detail.outcome], ["audit.tenant", detail.tenantId], ["audit.actor", actor(detail)], ["audit.session", detail.sessionId], ["audit.scheme", detail.authScheme], ["audit.correlation", detail.correlationId], ["audit.resource", [detail.resourceType, detail.resourceId].filter(Boolean).join(" / ")], ["audit.error", detail.errorCode]].map(([key, value]) => <div key={key}><dt title={t(key)}>{t(key)}</dt><dd><code>{value || t("common.none")}</code></dd></div>)}
        </dl>
        <p className="muted">{t("audit.immutable")}</p>
        <Button label="common.close" help="common.close" onClick={() => setDetail(null)} />
      </Modal>}
    </>
  );
}
