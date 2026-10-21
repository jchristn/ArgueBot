import { useTranslation } from "react-i18next";
import { useData } from "./useData.js";
import { Button } from "./ui.jsx";

export default function FailedTurn({ api, conversationId, message, onInspect }) {
  const { t } = useTranslation();
  const { data } = useData(message.id, async () => {
    let pageNumber = 1;
    while (true) {
      const page = await api.attempts(conversationId, message.runId, pageNumber, 100);
      const attempt = page.items.find(item => item.messageId === message.id);
      if (attempt || pageNumber * page.pageSize >= page.totalCount) return attempt || null;
      pageNumber += 1;
    }
  });
  return <div className="turn-failure" role="alert">
    <strong>{t("messages.failed")}</strong>
    <p>{data?.errorMessage || t("messages.failedHelp")}</p>
    {data?.errorCode && <p>{t("common.error")}: <code>{data.errorCode}</code></p>}
    <Button label="attempts.title" onClick={onInspect} />
  </div>;
}
