import { useEffect, useState } from "react";
import { Link, useNavigate, useOutletContext, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import ActionMenu from "../components/ActionMenu.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import { describe } from "../components/QuestionModal.jsx";
import { useQuestions } from "../components/QuestionHost.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal, Pagination } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";
import { savedPageSize } from "../utils/page-size.js";

const statuses = ["open", "resolved", "answered", "skipped", "expired", "cancelled", ""];

/** Relative age with the exact time on hover. */
function Age({ value }) {
  const { i18n } = useTranslation();
  const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
  const units = [["day", 86400], ["hour", 3600], ["minute", 60], ["second", 1]];
  const [unit, size] = units.find(([, length]) => Math.abs(seconds) >= length) || units.at(-1);
  const text = new Intl.RelativeTimeFormat(i18n.language.includes("-X") ? "en" : i18n.language, { numeric: "auto" }).format(Math.round(seconds / size), unit);
  return <time dateTime={value} title={formatDate(value)}>{text}</time>;
}

/** Questions agents asked, open first. Rows and the Answer action open the shared question modal; nothing is answered on this page. */
export default function Questions() {
  const { api } = useOutletContext();
  const { t } = useTranslation();
  const { open, revision, live } = useQuestions();
  const params = useParams();
  const navigate = useNavigate();
  const [status, setStatus] = useState("open");
  const [scope, setScope] = useState("mine");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(() => savedPageSize("questions", 25));
  const [json, setJson] = useState(null);
  const [viewing, setViewing] = useState(null);
  const { data, error, reload } = useData(`questions:${status}:${scope}:${page}:${pageSize}:${revision}`, () => api.questions({ status, scope, pageNumber: page, pageSize }));
  // A deep link opens that question in the modal.
  useEffect(() => {
    if (!params.id) return;
    open(params.id);
    navigate("/dashboard/questions", { replace: true });
  }, [navigate, open, params.id]);
  const items = data?.items || [];
  const filter = setter => value => { setter(value); setPage(1); };
  const show = item => item.status === "open" ? open(item) : setViewing(item);
  return <>
    <div className="page-heading">
      <div>
        <h1>{t("questions.title")}</h1>
        <p>{t("questions.description")}</p>
      </div>
      <span className={"badge " + (live ? "success" : "warning")} title={t("questions.live.help")}>{t(live ? "questions.live" : "questions.polling")}</span>
    </div>
    <div className="tab-toolbar">
      <Field label="questions.statusFilter">{props => <select {...props} value={status} onChange={event => filter(setStatus)(event.target.value)}>{statuses.map(value => <option key={value || "all"} value={value}>{t("questions.filter." + (value || "all"))}</option>)}</select>}</Field>
      <Field label="questions.scope">{props => <select {...props} value={scope} onChange={event => filter(setScope)(event.target.value)}><option value="mine">{t("questions.scope.mine")}</option><option value="tenant">{t("questions.scope.tenant")}</option></select>}</Field>
      <div className="toolbar-actions"><Button label="common.refresh" icon="refresh" iconOnly onClick={reload} /></div>
    </div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {!data && !error && <p className="muted" role="status">{t("common.loading")}</p>}
    {data && <p className="muted table-summary">{t("questions.showing", { from: data.totalCount === 0 ? 0 : (data.pageNumber - 1) * data.pageSize + 1, to: (data.pageNumber - 1) * data.pageSize + items.length, total: data.totalCount, page: data.pageNumber, pages: Math.max(1, Math.ceil(data.totalCount / data.pageSize)) })}</p>}
    {data && items.length > 0 && <div className="table-scroll"><table>
      <thead><tr>
        <th scope="col">{t("questions.column.question")}</th><th scope="col">{t("questions.column.asker")}</th><th scope="col">{t("questions.column.conversation")}</th>
        <th scope="col">{t("questions.column.status")}</th><th scope="col">{t("questions.column.age")}</th>
        <th scope="col" className="actions-cell">{t("common.actions")}</th>
      </tr></thead>
      <tbody>{items.map(item => <ObjectRow key={item.id} onOpen={() => show(item)}>
        <td>{item.questions.map(question => <span key={question.id} className="badge tag" title={question.prompt}>{question.header}</span>)}<div className="muted prompt-description" dir="auto">{item.questions[0]?.prompt}</div></td>
        <td>{item.askerName || item.askerId}</td>
        <td><Link to={"/dashboard/conversations/" + encodeURIComponent(item.conversationId)} onClick={event => event.stopPropagation()}>{item.conversationTitle || item.conversationId}</Link></td>
        <td><span className={"badge state-" + item.status}>{t("questions.status." + item.status)}</span>{item.status === "open" && item.holdState === "held" && <> <span className="badge warning">{t("questions.agentWaiting")}</span></>}</td>
        <td><Age value={item.createdUtc} /></td>
        <td className="actions-cell"><ActionMenu context={item.id} items={[
          ...(item.status === "open" ? [{ label: "questions.answer", onClick: () => open(item) }] : []),
          { label: "common.view", onClick: () => setViewing(item) },
          { label: "common.inspect", onClick: () => setJson(item) },
        ]} /></td>
      </ObjectRow>)}</tbody>
    </table></div>}
    {data && items.length === 0 && <div className="empty-state">{t(status === "open" ? "questions.emptyOpen" : "questions.empty")}</div>}
    {data && data.totalCount > 0 && <Pagination page={{ pageNumber: data.pageNumber, pageSize: data.pageSize, totalCount: data.totalCount }} table="questions" onChange={({ pageNumber, pageSize: size }) => { if (size && size !== pageSize) { setPageSize(size); setPage(1); } else setPage(pageNumber); }} />}
    {viewing && <Modal title="questions.viewTitle" onClose={() => setViewing(null)} wide>
      <p className="question-meta">{t("questions.askedBy", { name: viewing.askerName || viewing.askerId })} · <time dateTime={viewing.createdUtc}>{formatDate(viewing.createdUtc)}</time> · <span className={"badge state-" + viewing.status}>{t("questions.status." + viewing.status)}</span></p>
      {viewing.context && <p dir="auto">{viewing.context}</p>}
      <dl>{viewing.questions.map(question => <div key={question.id}><dt>{question.header}: <span dir="auto">{question.prompt}</span></dt><dd>{viewing.status === "open" ? t("questions.notYetAnswered") : describe(question.answer, t)}</dd></div>)}</dl>
      <footer className="dialog-actions">
        {viewing.status === "open" && <Button label="questions.answer" className="primary" onClick={() => { const item = viewing; setViewing(null); open(item); }} />}
        <Button label="common.close" onClick={() => setViewing(null)} />
      </footer>
    </Modal>}
    {json && <Modal title="common.inspect" onClose={() => setJson(null)} wide><pre className="json-view">{JSON.stringify(json, null, 2)}</pre><footer className="dialog-actions"><Button label="common.close" onClick={() => setJson(null)} /></footer></Modal>}
  </>;
}
