import { Fragment, useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Field, Modal, Pagination } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

export default function AttemptDialog({ conversationId, runs, messages, onClose }) {
  const { api } = useOutletContext();
  const { t } = useTranslation();
  const [runId, setRunId] = useState(runs.at(-1)?.id || "");
  const [paging, setPaging] = useState({ pageNumber: 1, pageSize: 25 });
  const [detail, setDetail] = useState(null);
  const run = runs.find((entry) => entry.id === runId);
  const { data, error, loading, reload } = useData(`${runId}:${paging.pageNumber}:${paging.pageSize}`, () => api.attempts(conversationId, runId, paging.pageNumber, paging.pageSize));
  const source = new Map(messages.map((message) => [message.id, message]));
  return <Modal title="attempts.title" onClose={onClose} wide>
    <p>{t("attempts.description")}</p>
    <Field label="attempts.run">
      {(props) => <select {...props} value={runId} onChange={(event) => { setRunId(event.target.value); setPaging((current) => ({ ...current, pageNumber: 1 })); setDetail(null); }}>
        {runs.map((entry) => <option key={entry.id} value={entry.id}>{formatDate(entry.createdUtc)} · {entry.id}</option>)}
      </select>}
    </Field>
    <p title={t("attempts.question")}><strong>{t("attempts.question")}: </strong>{run?.question}</p>
    <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
    {error && <ErrorPanel error={error} retry={reload} />}
    {loading && <p role="status">{t("common.loading")}</p>}
    {data && <>
      <Pagination page={data} onChange={(changes) => setPaging((current) => ({ ...current, ...changes }))} />
      <div className="table-scroll"><table>
        <thead><tr>{["attempts.number", "attempts.state", "attempts.participant", "common.actions"].map((key) => <th key={key} scope="col" title={t(key)}>{t(key)}</th>)}</tr></thead>
        <tbody>{data.items.map((entry) => <tr key={entry.id}>
          <td title={entry.id}>{entry.number}</td>
          <td><span className="badge" title={entry.errorCode || entry.state}>{t(`state.${entry.state}`, { defaultValue: entry.state })}</span></td>
          <td title={entry.participantId}>{run?.participants.find((participant) => participant.id === entry.participantId)?.name || entry.participantId}</td>
          <td><Button label="attempts.inspect" onClick={() => setDetail(entry)} /></td>
        </tr>)}</tbody>
      </table></div>
      {data.items.length === 0 && <p>{t("attempts.empty")}</p>}
    </>}
    {detail && <section className="inspector-section" aria-label={t("attempts.inspect")}>
      <h3>{t("attempts.number")} {detail.number}</h3>
      <dl className="server-details">
        {[["audit.id", detail.id], ["attempts.prompt", `${detail.promptVersion} / ${detail.promptDigest}`], ["attempts.duration", t("attempts.durationValue", { milliseconds: detail.durationMilliseconds })], ["attempts.characters", t("attempts.charactersValue", { count: detail.contextCharacters })], ["audit.error", detail.errorCode || t("common.none")]].map(([key, value]) => <Fragment key={key}><dt title={t(key)}>{t(key)}</dt><dd><code>{value}</code></dd></Fragment>)}
      </dl>
      <h4 title={t("attempts.context.help")}>{t("attempts.context")}</h4>
      <ol>{detail.contextMessageIds.map((id) => <li key={id}><code>{id}</code><p>{source.get(id)?.content ?? t("attempts.missing")}</p></li>)}</ol>
      {detail.contextMessageIds.length === 0 && <p>{t("attempts.pending")}</p>}
    </section>}
    <footer><Button label="common.close" help="common.close" onClick={onClose} /></footer>
  </Modal>;
}
