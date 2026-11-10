import { useState } from "react";
import { useTranslation } from "react-i18next";
import { notificationsEnabled, setNotificationsEnabled } from "./QuestionHost.jsx";

/** Per-browser choice to show desktop notifications when an agent asks a question while this tab is in the background. */
export default function QuestionNotifications() {
  const { t } = useTranslation();
  const supported = typeof window !== "undefined" && "Notification" in window;
  const [enabled, setEnabled] = useState(() => supported && notificationsEnabled() && Notification.permission === "granted");
  const [denied, setDenied] = useState(() => supported && Notification.permission === "denied");
  const toggle = async checked => {
    if (!checked) { setNotificationsEnabled(false); setEnabled(false); return; }
    const permission = Notification.permission === "granted" ? "granted" : await Notification.requestPermission();
    const granted = permission === "granted";
    setNotificationsEnabled(granted); setEnabled(granted); setDenied(permission === "denied");
  };
  return <section className="page-section">
    <header><h2>{t("account.notifications")}</h2></header>
    <p className="muted">{t("account.notifications.help")}</p>
    {!supported ? <p>{t("account.notifications.unsupported")}</p> : <label className="choice">
      <input type="checkbox" checked={enabled} disabled={denied} onChange={event => toggle(event.target.checked)} /> {t("account.notifications.enable")}
    </label>}
    {denied && <p className="warning-panel">{t("account.notifications.denied")}</p>}
  </section>;
}
