import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

export default function RunActivity({ run, messages, connection }) {
  const { t } = useTranslation();
  const [now, setNow] = useState(() => Date.now());
  const message = messages.findLast(entry => entry.runId === run?.id && entry.state === "streaming" && entry.kind !== "tool");
  const [lastChangeObserved, setLastChangeObserved] = useState(() => Date.now());
  const observedSize = message?.contentBytes ?? message?.content.length ?? 0;
  useEffect(() => { setLastChangeObserved(Date.now()); }, [message?.id, observedSize]);
  const active = run && ["agent_turn", "pausing"].includes(run.state);
  useEffect(() => {
    if (!active) return undefined;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [active]);
  if (!run || !["queued", "agent_turn", "pausing"].includes(run.state)) return null;
  const profile = run.protocolVersion === 2 ? (["question", "synthesis", "compaction"].includes(run.phase) ? run.orchestrator : run.participants[(run.speakerIndex + run.round - 1) % run.participants.length]) : run.participants[run.speakerIndex];
  const speaker = profile?.name || t("activity.agent");
  const streaming = message?.content.length > 0;
  const started = Date.parse(message?.createdUtc);
  const elapsed = Number.isFinite(started) ? Math.max(0, Math.floor((now - started) / 1000)) : null;
  const timeout = profile?.timeoutSeconds;
  const reconnecting = connection !== "common.connected";
  return <div className="run-activity" role="status">
    <span className={reconnecting ? "status-dot" : "activity-spinner"} aria-hidden="true" />
    <div>
      <strong>{reconnecting ? t("activity.reconnecting") : run.state === "queued" ? t("activity.queued") : t("activity.working", { speaker })}</strong>
      <p>{reconnecting ? t("activity.connectionHelp") : run.state === "pausing" ? t("activity.pausing") : streaming ? t("activity.streaming") : t("activity.waiting")}</p>
      {message && !reconnecting && <p>{t("activity.silence", { seconds: Math.max(0, Math.floor((now - lastChangeObserved) / 1000)) })}</p>}
      {elapsed !== null && timeout && <p>{t("activity.deadline", { elapsed, timeout })}</p>}
    </div>
  </div>;
}
