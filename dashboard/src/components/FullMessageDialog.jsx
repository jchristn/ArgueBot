import CopyButton from "./CopyButton.jsx";
import CopyableId from "./CopyableId.jsx";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Modal } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

export default function FullMessageDialog({ conversationId, messageId, onClose }) {
  const { api } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, loading, reload } = useData(messageId, () => api.message(conversationId, messageId));
  return <Modal title="messages.full" onClose={onClose} wide>
    {error && <ErrorPanel error={error} retry={reload} />}
    {loading && <p role="status">{t("common.loading")}</p>}
    {data && <>
      <p><strong>{data.speaker}</strong> · <time>{formatDate(data.createdUtc)}</time></p>
      <CopyableId value={data.id} />
      <CopyButton label="messages.copy" value={data.content} />
      <p className="muted">{t("messages.snapshot")}</p>
      <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
      <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{data.content}</Markdown></div>
    </>}
    <footer><Button label="common.close" help="common.close" onClick={onClose} /></footer>
  </Modal>;
}
