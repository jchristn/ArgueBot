import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import CopyButton from "./CopyButton.jsx";
import CopyableId from "./CopyableId.jsx";
import FailedTurn from "./FailedTurn.jsx";
import MessageBody from "./MessageBody.jsx";
import { Button, Icon, speakerLabel } from "./ui.jsx";
import { messageContent } from "../utils/message-content.js";
import { formatDate } from "../i18n/index.js";
import ResourceToolLink from "./ResourceToolLink.jsx";

/** The step label shown for a message, matching the card view. */
export function kindLabel(t, message) {
  const key = message.kind === "tool" && message.content.startsWith("Conversation tool:") ? "contextTool" : message.kind === "question" && message.speaker !== "user" ? "orchestratorQuestion" : message.kind;
  return t("state." + key, { defaultValue: message.kind });
}

/** A completed vote's position and rationale, or null for any other turn. */
export function votePosition(message) {
  if (message.kind !== "ratification" || message.state !== "completed" || message.contentTruncated) return null;
  try { const value = JSON.parse(message.content); return typeof value.position === "string" ? { position: value.position, rationale: typeof value.rationale === "string" ? value.rationale : "" } : null; }
  catch { return null; }
}

/** Markdown reduced to readable text: code fences, heading and quote markers, emphasis, links and table pipes removed; words and identifiers kept. */
export function plainText(markdown) {
  return markdown
    .replace(/```[\s\S]*?```/g, " ")
    .replace(/^\s{0,3}(#{1,6}|>)\s*/gm, "")
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/(\*\*|__|\*|`)(?=\S)([\s\S]*?\S)\1/g, "$2")
    .replace(/^\s*\|?\s*:?-{3,}.*$/gm, " ")
    .replace(/\|/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

/** One line of plain text for a timeline row. Streaming turns show their newest text. */
export function rowPreview(message) {
  const text = plainText(messageContent(message) || "");
  if (message.state === "streaming" && text.length > 240) return "…" + text.slice(-240);
  return text.slice(0, 400);
}

/**
 * Group the transcript for compact monitoring: consecutive tool records from one speaker become one row,
 * and a heading marks where each debate round starts. Rounds come from the service (`round` on each generated
 * message), so a page that starts mid-debate shows the real round. Follow-up runs get no round headings.
 * Messages without a round fall back to counting orchestrator questions.
 */
export function timelineRows(messages, followupRunIds = new Set()) {
  const rows = [];
  let round = 0;
  let heading = "";
  for (const message of messages) {
    const last = rows.at(-1);
    if (message.kind === "tool" && last?.type === "tools" && last.speaker === message.speaker) { last.messages.push(message); continue; }
    if (!followupRunIds.has(message.runId)) {
      if (Number.isInteger(message.round)) {
        const key = (message.runId || "") + ":" + message.round;
        if (key !== heading) { heading = key; round = message.round; rows.push({ type: "round", key: "round-" + message.id, round }); }
      }
      else if (message.kind === "question" && message.speaker !== "user") { heading = ""; rows.push({ type: "round", key: "round-" + message.id, round: ++round }); }
    }
    if (message.kind === "tool") rows.push({ type: "tools", key: "tools-" + message.id, id: message.id, speaker: message.speaker, messages: [message] });
    else rows.push({ type: "turn", key: message.id, id: message.id, message });
  }
  return rows;
}

/** A compact, expandable timeline of debate turns for watching a debate unfold. */
export default function TranscriptTimeline({ api, conversationId, messages, followupRunIds, hues, expanded, onToggle, jumpTarget, onFull, onInspect }) {
  const { t } = useTranslation();
  const rows = timelineRows(messages, followupRunIds);
  return <ol className="timeline">
    {rows.map(row => {
      if (row.type === "round") return <li key={row.key} className="timeline-round" aria-hidden="true"><span>{t("timeline.round", { round: row.round })}</span></li>;
      const open = expanded.has(row.id);
      const message = row.type === "turn" ? row.message : row.messages.at(-1);
      const hue = hues.get(message.speaker);
      const failed = row.type === "turn" && ["failed", "interrupted"].includes(message.state);
      const streaming = message.state === "streaming";
      const style = hue !== undefined ? { "--speaker-hue": hue } : undefined;
      const label = speakerLabel(t, message.speaker);
      return <li key={row.key} id={"message-" + row.id} className={"timeline-row" + (message.speaker === "user" ? " user-row" : "") + (open ? " open" : "") + (failed ? " failed" : "") + (jumpTarget === row.id ? " jump-target" : "")} style={style}>
        <button type="button" className="timeline-summary message-toggle-row" aria-expanded={open} aria-controls={"timeline-body-" + row.id} onClick={() => onToggle(row.id)}>
          <span className="timeline-chevron"><Icon name="chevron" /></span>
          <strong className="timeline-speaker">{label}</strong>
          <span className="badge">{row.type === "tools" ? t("timeline.tools", { count: row.messages.length }) : kindLabel(t, message)}</span>
          {failed && <span className="badge failure">{t(`state.${message.state}`, { defaultValue: message.state })}</span>}
          {streaming && <span className="stream-dot" title={t("state.streaming")} />}
          {row.type === "turn" && votePosition(message) && <span className={"badge vote-" + votePosition(message).position}>{t(`position.${votePosition(message).position}`, { defaultValue: votePosition(message).position })}</span>}
          <span className="timeline-preview">{row.type === "turn" && votePosition(message) ? votePosition(message).rationale : row.type === "tools" ? row.messages.map(item => item.content.split("\n")[0].replace(/^Conversation tool: |^Local tool: /, "")).join(" · ") : rowPreview(message)}</span>
          <time title={formatDate(message.createdUtc)}>{new Date(message.createdUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}</time>
        </button>
        {open && <div className="timeline-body" id={"timeline-body-" + row.id}>
          {row.type === "tools"
            ? row.messages.map(item => <details key={item.id} className="tool-evidence"><summary>{item.content.split("\n").slice(0, 2).join(" · ")}</summary><ResourceToolLink content={item.content} /><pre>{item.content}</pre></details>)
            : <>
              <div className="timeline-actions">
                <span className="muted">{formatDate(message.createdUtc)}</span>
                <CopyableId value={message.id} />
                <CopyButton label="messages.copy" iconOnly loadText={async () => (await api.message(conversationId, message.id)).content} />
                <Button label="messages.window" icon="external" onClick={() => onFull(message.id)} />
              </div>
              <MessageBody api={api} conversationId={conversationId} message={message} />
              {failed && <FailedTurn api={api} conversationId={conversationId} message={message} onInspect={() => onInspect(message.runId)} />}
            </>}
        </div>}
      </li>;
    })}
  </ol>;
}

/** The agent the debate is waiting on, using the same speaker rule as the service. */
export function currentSpeaker(run) {
  if (!run) return null;
  if (run.protocolVersion !== 2) return run.participants?.[run.speakerIndex] || null;
  if (["question", "synthesis", "compaction"].includes(run.phase)) return run.orchestrator || null;
  return run.participants?.length ? run.participants[(run.speakerIndex + run.round - 1) % run.participants.length] : null;
}

/** What the current turn has produced so far: when it started and its newest visible activity. */
export function liveActivity(run, messages) {
  const speaker = currentSpeaker(run)?.name;
  const own = messages.filter(message => message.runId === run.id);
  let start = own.length;
  while (start > 0 && (own[start - 1].kind === "tool" || own[start - 1].state === "streaming") && own[start - 1].speaker === speaker) start--;
  const current = own.slice(start);
  return { speaker, started: current[0]?.createdUtc || null, latest: current.at(-1) || null, toolCalls: current.filter(message => message.kind === "tool" && message.content.includes("Status: completed")).length };
}

/** A live row at the end of the transcript while an agent is working, so progress is visible without scrolling up. */
export function LiveTurn({ run, messages, hues }) {
  const { t } = useTranslation();
  const [now, setNow] = useState(() => Date.now());
  const working = run && ["queued", "agent_turn", "pausing"].includes(run.state);
  useEffect(() => {
    if (!working) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [working]);
  if (!working) return null;
  const activity = liveActivity(run, messages);
  const elapsed = activity.started ? Math.max(0, Math.floor((now - Date.parse(activity.started)) / 1000)) : null;
  const latest = activity.latest;
  const detail = !latest || (latest.kind !== "tool" && !rowPreview(latest)) ? t("timeline.live.noOutput")
    : latest.kind === "tool" ? t("timeline.live.tool", { tool: latest.content.split("\n")[0].replace(/^Conversation tool: |^Local tool: /, ""), count: activity.toolCalls })
    : t("timeline.live.writing", { text: rowPreview(latest).slice(-160) });
  const hue = hues.get(activity.speaker);
  return <div className="timeline-row live-turn" role="status" aria-live="polite" style={hue !== undefined ? { "--speaker-hue": hue } : undefined}>
    <div className="timeline-summary">
      <span className="activity-spinner" aria-hidden="true" />
      <strong className="timeline-speaker">{run.state === "queued" ? t("activity.queued") : activity.speaker || t("activity.agent")}</strong>
      {run.state !== "queued" && <span className="badge">{t(`prompts.purpose.${run.protocolVersion === 2 ? run.phase : "response"}`, { defaultValue: run.phase })}</span>}
      <span className="timeline-preview">{run.state === "pausing" ? t("activity.pausing") : detail}</span>
      {elapsed !== null && <time>{t("timeline.live.elapsed", { minutes: Math.floor(elapsed / 60), seconds: String(elapsed % 60).padStart(2, "0") })}</time>}
    </div>
  </div>;
}
