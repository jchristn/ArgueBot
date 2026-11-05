import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import CopyableId from "../components/CopyableId.jsx";
import DetailsDialog from "../components/DetailsDialog.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal, Pagination } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";

export const auditOperations = ["AuthSuccess", "AuthFailure", "SessionIssued", "SessionRefreshed", "SessionRevoked", "CredentialUsed", "CredentialCreated", "CredentialUpdated", "CredentialDisabled", "CredentialDeleted", "AuthorizationDenied", "AuthorizationBypass", "PasswordChanged", "UserCreated", "UserUpdated", "UserDeleted", "TenantCreated", "TenantUpdated", "TenantDeleted", "AuditPruned"];
const retention = [30, 90, 365];

/** Security audit log: sign-ins, sessions, credentials, identity changes and every denied request. */
export default function Audit() {
  const { api, notify, session } = useOutletContext();
  const { t } = useTranslation();
  const [filters, setFilters] = useState({ operation: "", outcome: "", pageNumber: 1, pageSize: 50 });
  const { data, error, reload } = useData("audit:" + JSON.stringify(filters), () => api.audit(filters));
  const [pruning, setPruning] = useState(null);
  const [failure, setFailure] = useState(null);
  const [viewing, setViewing] = useState(null);
  const change = value => setFilters({ ...filters, ...value, pageNumber: value.pageNumber || 1 });
  return <>
    <div className="page-heading"><div><h1>{t("audit.title")}</h1><p>{t(session?.user?.isAdmin ? "audit.description.system" : "audit.description")}</p></div></div>
    <div className="tab-toolbar">
      <Field label="audit.operation">{props => <select {...props} value={filters.operation} onChange={event => change({ operation: event.target.value })}><option value="">{t("audit.allOperations")}</option>{auditOperations.map(item => <option key={item} value={item}>{item}</option>)}</select>}</Field>
      <Field label="audit.outcome">{props => <select {...props} value={filters.outcome} onChange={event => change({ outcome: event.target.value })}><option value="">{t("audit.allOutcomes")}</option>{["success", "failure", "denied"].map(item => <option key={item} value={item}>{t("audit.outcome." + item)}</option>)}</select>}</Field>
      <div className="toolbar-actions">
        <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
        <Button label="audit.prune" onClick={() => { setFailure(null); setPruning(90); }} />
      </div>
    </div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && data.items.length === 0 && <div className="empty-state">{t("audit.empty")}</div>}
    {data && data.items.length > 0 && <div className="table-scroll"><table className="audit-table">
      <thead><tr><th scope="col">{t("audit.time")}</th><th scope="col">{t("audit.event")}</th><th scope="col">{t("audit.outcome")}</th><th scope="col">{t("audit.who")}</th><th scope="col">{t("audit.request")}</th><th scope="col">{t("audit.reason")}</th><th scope="col">{t("audit.requestId")}</th></tr></thead>
      <tbody>{data.items.map(item => <ObjectRow key={item.id} inspect onOpen={() => setViewing(item)}>
        <td><time>{formatDate(item.createdUtc)}</time></td>
        <td><strong>{item.operation}</strong>{item.errorCode && <div className="muted"><code>{item.errorCode}</code></div>}</td>
        <td><span className={item.outcome === "success" ? "badge success" : "badge failure"}>{t("audit.outcome." + item.outcome, { defaultValue: item.outcome })}</span>{item.statusCode > 0 && <div className="muted">{item.statusCode}</div>}</td>
        <td>{item.userId ? <code>{item.userId}</code> : <span className="muted">{t("audit.anonymous")}</span>}{item.principalType && <div className="muted">{item.principalType}{item.authScheme ? " · " + item.authScheme : ""}</div>}{session?.user?.isAdmin && item.tenantId && <div className="muted"><code>{item.tenantId}</code></div>}</td>
        <td>{item.method && <code>{item.method} {item.path}</code>}{item.sourceIp && <div className="muted"><code>{item.sourceIp}</code></div>}</td>
        <td className="audit-reason">{item.reason}</td>
        <td>{item.correlationId && <CopyableId value={item.correlationId} />}</td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {data && <Pagination page={data} onChange={change} />}
    {viewing && <DetailsDialog title="audit.details" heading={`${viewing.operation} · ${formatDate(viewing.createdUtc)}`} record={viewing} prefix="audit" onClose={() => setViewing(null)} />}
    {pruning && <Modal title="audit.prune" onClose={() => setPruning(null)}>
      <p>{t("audit.prune.help")}</p>
      <Field label="audit.prune.age">{props => <select {...props} value={pruning} onChange={event => setPruning(Number(event.target.value))}>{retention.map(days => <option key={days} value={days}>{t("audit.prune.days", { days })}</option>)}</select>}</Field>
      {failure && <ErrorPanel error={failure} />}
      <footer className="dialog-actions"><Button label="common.cancel" onClick={() => setPruning(null)} /><Button label="audit.prune.confirm" className="danger" onClick={async () => { try { const result = await api.deleteAudit(new Date(Date.now() - pruning * 86400000).toISOString()); setPruning(null); notify({ message: t("audit.prune.done", { count: result.deleted }) }); await reload(); } catch (problem) { setFailure(problem); } }} /></footer>
    </Modal>}
  </>;
}
