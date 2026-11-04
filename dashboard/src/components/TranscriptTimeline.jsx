import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useTranslation } from "react-i18next";
import CopyButton from "./CopyButton.jsx";
import CopyableId from "./CopyableId.jsx";
import FailedTurn from "./FailedTurn.jsx";
import { Button, Icon, speakerLabel } from "./ui.jsx";
import { messageContent } from "../utils/message-content.js";
import { formatDate } from "../i18n/index.js";

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

/** One line of plain text for a timeline row. Streaming turns show their newest text. */
export function rowPreview(message) {
  const text = (messageContent(message) || "").replace(/```[\s\S]*?```/g, " ").replace(/[#*_>`|[\]()]+/g, " ").replace(/\s+/g, " ").trim();
  if (message.state === "streaming" && text.length > 240) return "…" + text.slice(-240);
  return text.slice(0, 400);
}

/**
 * Group the transcript for compact monitoring: consecutive tool records from one speaker become one row,
 * and each orchestrator question starts a numbered round.
 */
export function timelineRows(messages) {
  const rows = [];
  let round = 0;
  for (const message of messages) {
    const last = rows.at(-1);
    if (message.kind === "tool" && last?.type === "tools" && last.speaker === message.speaker) { last.messages.push(message); continue; }
    if (message.kind === "question" && message.speaker !== "user") rows.push({ type: "round", key: "round-" + message.id, round: ++round });
    if (message.kind === "tool") rows.push({ type: "tools", key: "tools-" + message.id, id: message.id, speaker: message.speaker, messages: [message] });
    else rows.push({ type: "turn", key: message.id, id: message.id, message });
  }
  return rows;
}

/** A compact, expandable timeline of debate turns for watching a debate unfold. */
export default function TranscriptTimeline({ api, conversationId, messages, hues, expanded, onToggle, jumpTarget, onFull, onInspect }) {
  const { t } = useTranslation();
  const rows = timelineRows(messages);
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
            ? row.messages.map(item => <details key={item.id} className="tool-evidence"><summary>{item.content.split("\n").slice(0, 2).join(" · ")}</summary><pre>{item.content}</pre></details>)
            : <>
              <div className="timeline-actions">
                <span className="muted">{formatDate(message.createdUtc)}</span>
                <CopyableId value={message.id} />
                <CopyButton label="messages.copy" iconOnly loadText={async () => (await api.message(conversationId, message.id)).content} />
                {message.contentTruncated && <Button label="messages.full" onClick={() => onFull(message.id)} />}
              </div>
              {message.contentTruncated && <p className="muted">{t("messages.preview", { bytes: message.contentBytes })}</p>}
              <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{messageContent(message)}</Markdown></div>
              {failed && <FailedTurn api={api} conversationId={conversationId} message={message} onInspect={() => onInspect(message.runId)} />}
            </>}
        </div>}
      </li>;
    })}
  </ol>;
}
