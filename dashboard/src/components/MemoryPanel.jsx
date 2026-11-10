import { useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Button, ErrorPanel, Modal, speakerLabel } from "./ui.jsx";
import { useData } from "./useData.js";
import MemorySummary from "./MemorySummary.jsx";

export default function MemoryPanel({ api, id, run, notify, onChange }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [sourcePages, setSourcePages] = useState({});
  const [source, setSource] = useState(null);
  const [ancestor, setAncestor] = useState(null);
  const [busy, setBusy] = useState(false);
  const { data, error, reload } = useData(`${run.id}:${run.revision}:${page}`, async () => {
    return { summaries: await api.summaries(id, run.id, page) };
  });
  const perform = async operation => {
    setBusy(true);
    try { await operation(); await reload(); await onChange(); }
    catch (failure) { notify(failure); }
    finally { setBusy(false); }
  };
  const renderSummary = summary => <MemorySummary key={summary.id} summary={summary} run={run} busy={busy}
    onSource={sourceId => perform(async () => setSource({ ...await api.message(id, sourceId), digest: summary.sources?.find(item => item.messageId === sourceId)?.digest }))}
    onParent={parentId => perform(async () => { const [parent] = await api.summary(id, run.id, parentId); setAncestor(parent); })}
    onPage={pageNumber => perform(async () => { const [next] = await api.summary(id, run.id, summary.id, pageNumber); setSourcePages(current => ({ ...current, [summary.id]: next })); if (ancestor?.id === summary.id) setAncestor(next); })} />;
  const canChange = ["paused", "window", "queued"].includes(run.state);
  return <section aria-label={t("memory.title")} className="memory-panel">
    {error && <ErrorPanel error={error} retry={reload} />}
    <details><summary>{t("memory.title")}</summary><p className="muted">{t("memory.help")}</p>
      <div className="button-group"><Button label="memory.rebuild" disabled={!canChange || busy} onClick={() => perform(() => api.action(id, run.id, { action: "rebuild_summary", revision: run.revision }))} /><Button label="memory.exclude" disabled={!canChange || busy} onClick={() => perform(() => api.action(id, run.id, { action: "exclude_summary", revision: run.revision }))} /></div>
      {!canChange && <p className="muted">{t("memory.pauseHelp")}</p>}
      {data?.summaries.length === 0 && <p>{t("memory.empty")}</p>}
      {data?.summaries.map(original => renderSummary(sourcePages[original.id] || original))}
      <div className="button-group"><Button label="common.previous" disabled={page <= 1 || busy} onClick={() => setPage(page - 1)} /><span>{t("memory.page", { page })}</span><Button label="common.next" disabled={data?.summaries.length !== 25 || busy} onClick={() => setPage(page + 1)} /></div>
    </details>
    {ancestor && <Modal title="memory.provenance" onClose={() => setAncestor(null)} wide>{renderSummary(ancestor)}<Button label="common.close" onClick={() => setAncestor(null)} /></Modal>}
    {source && <Modal title="memory.original" onClose={() => setSource(null)} wide><p>{t("memory.sourceHeading", { speaker: speakerLabel(t, source.speaker), sequence: source.sequence })}</p>{source.digest && <p className="source-digest">{t("memory.digest")}<br /><code>{source.digest}</code></p>}<div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{source.content}</Markdown></div><footer className="dialog-actions"><Button label="common.close" onClick={() => setSource(null)} /></footer></Modal>}
  </section>;
}
