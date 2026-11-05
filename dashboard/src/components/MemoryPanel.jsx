import { useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Button, ErrorPanel, Field, Modal, speakerLabel } from "./ui.jsx";
import { useData } from "./useData.js";
import MemorySummary from "./MemorySummary.jsx";
import { randomId } from "../utils/random-id.js";

export default function MemoryPanel({ api, id, run, notify, onChange }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [sourcePages, setSourcePages] = useState({});
  const [source, setSource] = useState(null);
  const [ancestor, setAncestor] = useState(null);
  const [answer, setAnswer] = useState(null);
  const [text, setText] = useState("");
  const [delivery, setDelivery] = useState("");
  const [key, setKey] = useState(randomId);
  const [busy, setBusy] = useState(false);
  const { data, error, reload } = useData(`${run.id}:${run.revision}:${page}`, async () => {
    const [summaries, clarifications] = await Promise.all([api.summaries(id, run.id, page), api.clarifications(id, run.id)]);
    return { summaries, clarifications };
  });
  const perform = async operation => {
    setBusy(true);
    try { await operation(); await reload(); await onChange(); }
    catch (failure) { notify(failure); }
    finally { setBusy(false); }
  };
  const submit = skip => perform(async () => {
    await api.interject(id, { runId: run.id, text: skip ? t("memory.skipped", { question: answer.question }) : text, clarificationId: answer.id, skipClarification: skip, deliveryMode: delivery, pinned: true, idempotencyKey: key + (skip ? ":skip" : ":answer") });
    setAnswer(null); setText(""); setDelivery(""); setKey(randomId());
  });
  const renderSummary = summary => <MemorySummary key={summary.id} summary={summary} run={run} busy={busy}
    onSource={sourceId => perform(async () => setSource({ ...await api.message(id, sourceId), digest: summary.sources?.find(item => item.messageId === sourceId)?.digest }))}
    onParent={parentId => perform(async () => { const [parent] = await api.summary(id, run.id, parentId); setAncestor(parent); })}
    onPage={pageNumber => perform(async () => { const [next] = await api.summary(id, run.id, summary.id, pageNumber); setSourcePages(current => ({ ...current, [summary.id]: next })); if (ancestor?.id === summary.id) setAncestor(next); })} />;
  const canChange = ["paused", "window", "queued"].includes(run.state);
  return <section aria-label={t("memory.title")} className="memory-panel">
    {error && <ErrorPanel error={error} retry={reload} />}
    {data?.clarifications.filter(item => item.status === "open").map(item => <article className="clarification" key={item.id}>
      <strong>{t(item.blocking ? "memory.blocking" : "memory.request")}</strong><p>{item.question}</p>
      <Button label="memory.answer" disabled={busy} onClick={() => { setAnswer(item); setText(""); setKey(randomId()); }} />
    </article>)}
    <details><summary>{t("memory.title")}</summary><p className="muted">{t("memory.help")}</p>
      <div className="button-group"><Button label="memory.rebuild" disabled={!canChange || busy} onClick={() => perform(() => api.action(id, run.id, { action: "rebuild_summary", revision: run.revision }))} /><Button label="memory.exclude" disabled={!canChange || busy} onClick={() => perform(() => api.action(id, run.id, { action: "exclude_summary", revision: run.revision }))} /></div>
      {!canChange && <p className="muted">{t("memory.pauseHelp")}</p>}
      {data?.summaries.length === 0 && <p>{t("memory.empty")}</p>}
      {data?.summaries.map(original => renderSummary(sourcePages[original.id] || original))}
      <div className="button-group"><Button label="common.previous" disabled={page <= 1 || busy} onClick={() => setPage(page - 1)} /><span>{t("memory.page", { page })}</span><Button label="common.next" disabled={data?.summaries.length !== 25 || busy} onClick={() => setPage(page + 1)} /></div>
      {data?.clarifications.some(item => item.status !== "open") && <details><summary>{t("memory.resolved")}</summary>{data.clarifications.filter(item => item.status !== "open").map(item => <p key={item.id}>{item.question} · {t(`memory.${item.status}`)}</p>)}</details>}
    </details>
    {ancestor && <Modal title="memory.provenance" onClose={() => setAncestor(null)} wide>{renderSummary(ancestor)}<Button label="common.close" onClick={() => setAncestor(null)} /></Modal>}
    {source && <Modal title="memory.original" onClose={() => setSource(null)} wide><p>{t("memory.sourceHeading", { speaker: speakerLabel(t, source.speaker), sequence: source.sequence })}</p>{source.digest && <p className="source-digest">{t("memory.digest")}<br /><code>{source.digest}</code></p>}<div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{source.content}</Markdown></div><footer className="dialog-actions"><Button label="common.close" onClick={() => setSource(null)} /></footer></Modal>}
    {answer && <Modal title="memory.answer" onClose={() => !busy && setAnswer(null)}><form onSubmit={event => { event.preventDefault(); submit(false); }}>
      <p>{answer.question}</p>
      <Field label="memory.context">{props => <textarea {...props} required rows={5} value={text} onChange={event => { setText(event.target.value); setKey(randomId()); }} />}</Field>
      <Field label="panel.delivery">{props => <select {...props} required className={delivery ? "" : "needs-choice"} value={delivery} onChange={event => { setDelivery(event.target.value); setKey(randomId()); }}><option value="" disabled>{t("delivery.choose")}</option><option value="next_turn">{t("delivery.next_turn")}</option><option value="interrupt_now">{t("delivery.interrupt_now")}</option></select>}</Field>
      <p className="muted">{t("memory.answerHelp")}</p>
      <footer className="dialog-actions"><Button label="common.cancel" type="button" disabled={busy} onClick={() => setAnswer(null)} /><Button label="memory.skip" type="button" disabled={busy || !delivery} onClick={() => submit(true)} /><Button label="memory.send" type="submit" disabled={busy || !delivery || !text.trim()} className="primary" /></footer>
    </form></Modal>}
  </section>;
}
