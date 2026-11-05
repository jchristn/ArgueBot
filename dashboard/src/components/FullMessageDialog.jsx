import { messageContent } from "../utils/message-content.js";
import CopyButton from "./CopyButton.jsx";
import CopyableId from "./CopyableId.jsx";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Modal, speakerLabel } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

export default function FullMessageDialog({ conversationId, messageId, onClose }) {
  const { api } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, loading, reload } = useData(messageId, () => api.message(conversationId, messageId));
  return <Modal title="messages.full" onClose={onClose} wide className="message-reader">
    {error && <ErrorPanel error={error} retry={reload} />}
    {loading && <p role="status">{t("common.loading")}</p>}
    {data && <>
      <div className="reader-meta">
        <strong>{speakerLabel(t, data.speaker)}</strong>
        <span className="badge">{t("state." + data.kind, { defaultValue: data.kind })}</span>
        <time>{formatDate(data.createdUtc)}</time>
        <CopyableId value={data.id} />
        <span className="reader-actions"><CopyButton label="messages.copy" value={data.content} /><Button label="common.refresh" icon="refresh" iconOnly onClick={reload} /></span>
      </div>
      <p className="muted reader-note">{t("messages.snapshot")}</p>
      <article className="markdown reader-body"><Markdown remarkPlugins={[remarkGfm]}>{messageContent(data)}</Markdown></article>
    </>}
    <footer className="dialog-actions"><Button label="common.close" help="common.close" onClick={onClose} /></footer>
  </Modal>;
}
