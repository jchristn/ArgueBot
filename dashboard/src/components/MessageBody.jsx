import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { messageContent } from "../utils/message-content.js";

/**
 * A turn's rendered markdown. Transcript pages carry a bounded preview of long turns; when a finished turn
 * was cut short, its complete text is fetched so it always reads in full.
 */
export default function MessageBody({ api, conversationId, message }) {
  const { t } = useTranslation();
  const needsFull = message.contentTruncated && message.state !== "streaming";
  const [full, setFull] = useState(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!needsFull) return undefined;
    let cancelled = false;
    setFailed(false);
    api.message(conversationId, message.id).then(value => { if (!cancelled) setFull(value); }).catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [api, conversationId, message.id, message.contentBytes, needsFull]);
  const shown = needsFull && full ? full : message;
  return <>
    {needsFull && !full && <p className="muted" role="status">{t(failed ? "messages.fullFailed" : "messages.loadingFull", { bytes: message.contentBytes })}</p>}
    <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{messageContent(shown)}</Markdown></div>
  </>;
}
