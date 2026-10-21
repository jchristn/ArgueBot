import { useTranslation } from "react-i18next";

export default function RunActivity({ run, messages, connection }) {
  const { t } = useTranslation();
  if (!run || !["queued", "agent_turn", "pausing"].includes(run.state)) return null;
  const speaker = run.participants[run.speakerIndex]?.name || t("activity.agent");
  const streaming = messages.some(message => message.runId === run.id && message.state === "streaming" && message.content.length > 0);
  const reconnecting = connection !== "common.connected";
  return <div className="run-activity" role="status">
    <span className={reconnecting ? "status-dot" : "activity-spinner"} aria-hidden="true" />
    <div>
      <strong>{reconnecting ? t("activity.reconnecting") : run.state === "queued" ? t("activity.queued") : t("activity.working", { speaker })}</strong>
      <p>{reconnecting ? t("activity.connectionHelp") : run.state === "pausing" ? t("activity.pausing") : streaming ? t("activity.streaming") : t("activity.waiting")}</p>
    </div>
  </div>;
}
