import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Button } from "./ui.jsx";

const sourcePattern = /\b(msg_[A-Za-z0-9_-]+)\b/g;

export default function MemorySummary({ summary, run, busy, onSource, onParent, onPage }) {
  const { t } = useTranslation();
  // Summaries cite pinned constraints by source ID; make each citation open its original.
  const linked = summary.content.replace(sourcePattern, "[$1](#source:$1)");
  const components = { a: ({ href, children }) => href?.startsWith("#source:") ? <button type="button" className="link-button" disabled={busy} title={t("memory.openSource")} onClick={() => onSource(href.slice(8))}>{children}</button> : <a href={href}>{children}</a> };
  return <article className="proposal">
    <span className="badge">{t(summary.active ? "memory.active" : "memory.inactive")}</span>
    <p className="muted">{t(summary.recipientId ? "memory.privatePartition" : "memory.sharedPartition", { recipient: run.participants.find(item => item.id === summary.recipientId)?.name || summary.recipientId })}</p>
    <div className="markdown"><Markdown remarkPlugins={[remarkGfm]} components={components}>{linked}</Markdown></div>
    <details><summary>{t("memory.provenance")}</summary>
      <p className="source-digest">{t("memory.summaryId")}: <code>{summary.id}</code><br />{t("memory.attemptId")}: <code>{summary.attemptId}</code></p>
      <p>{t("memory.hashHelp")}</p>
      <p>{t("memory.parents")}</p>
      {summary.parentSummaryIds?.length ? <ul>{summary.parentSummaryIds.map(parentId => <li key={parentId}><button className="button" disabled={busy} onClick={() => onParent(parentId)}>{parentId}</button></li>)}</ul> : <p className="muted">{t("memory.noParents")}</p>}
    </details>
    <details><summary>{t("memory.sources", { count: summary.sourceCount })}</summary>
      <ol className="source-list" start={(summary.sourcePageNumber - 1) * 100 + 1}>{summary.sourceIds.map(sourceId => <li key={sourceId}><button className="button" disabled={busy} onClick={() => onSource(sourceId)}>{sourceId}</button></li>)}</ol>
      <div className="button-group"><Button label="common.previous" disabled={busy || summary.sourcePageNumber <= 1} onClick={() => onPage(summary.sourcePageNumber - 1)} /><span>{t("memory.page", { page: summary.sourcePageNumber })}</span><Button label="common.next" disabled={busy || summary.sourcePageNumber * 100 >= summary.sourceCount} onClick={() => onPage(summary.sourcePageNumber + 1)} /></div>
    </details>
  </article>;
}
