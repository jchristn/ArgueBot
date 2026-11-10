import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuestions } from "./QuestionHost.jsx";
import { Button } from "./ui.jsx";

/** Shown above the transcript while the run waits for the user's answer. The Answer button opens the question modal. */
export default function WaitingBanner({ api, conversationId, run }) {
  const { t } = useTranslation();
  const { open, revision } = useQuestions();
  const [waiting, setWaiting] = useState([]);
  const awaiting = run?.state === "awaiting_input";
  useEffect(() => {
    if (!awaiting) { setWaiting([]); return undefined; }
    let active = true;
    api.runQuestions(conversationId, run.id).then(items => { if (active) setWaiting(items.filter(item => item.status === "open" && item.wait)); }).catch(() => {});
    return () => { active = false; };
  }, [api, awaiting, conversationId, run?.id, run?.revision, revision]);
  if (!awaiting || waiting.length === 0) return null;
  const first = waiting[0];
  return <div className="waiting-banner" role="status">
    <span>{t("questions.waiting", { name: first.askerName || first.askerId, count: waiting.length })}</span>
    <Button label="questions.answer" className="primary" onClick={() => open(first)} />
  </div>;
}
