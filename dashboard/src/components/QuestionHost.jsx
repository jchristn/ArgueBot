import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import QuestionModal from "./QuestionModal.jsx";
import { Button } from "./ui.jsx";

const QuestionContext = createContext({ available: false, summary: null, open: () => {}, refresh: () => {}, revision: 0, live: false });

/** Open questions, the shared modal and change notifications for every page. */
export function useQuestions() { return useContext(QuestionContext); }

const POLL_MILLISECONDS = 5000;
const NOTIFY_KEY = "arguebot.questionNotifications";

/** Whether this browser should show desktop notifications for new questions. A per-viewer convenience. */
export function notificationsEnabled() {
  try { return localStorage.getItem(NOTIFY_KEY) === "on"; } catch { return false; }
}
export function setNotificationsEnabled(value) {
  try { if (value) localStorage.setItem(NOTIFY_KEY, "on"); else localStorage.removeItem(NOTIFY_KEY); } catch { /* storage unavailable */ }
}

/**
 * Watches the tenant's question changes, keeps the open count, and owns the one question modal.
 * The modal opens by itself only on the conversation whose agent is waiting; elsewhere a toast offers Answer.
 */
export default function QuestionHost({ api, children }) {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const [summary, setSummary] = useState(null);
  const [queue, setQueue] = useState([]);
  const [index, setIndex] = useState(0);
  const [toast, setToast] = useState(null);
  const [revision, setRevision] = useState(0);
  const [live, setLive] = useState(true);
  const cursor = useRef("");
  const prompted = useRef(new Set());
  const path = useRef(location.pathname);
  path.current = location.pathname;
  const baseTitle = useRef(document.title);

  const refresh = useCallback(async () => {
    try { setSummary(await api.questionSummary()); } catch { /* the next poll retries */ }
  }, [api]);

  /** Open the modal on one request, queueing the conversation's other open requests behind it. */
  const open = useCallback(async (requestOrId) => {
    const id = typeof requestOrId === "string" ? requestOrId : requestOrId.id;
    const conversationId = typeof requestOrId === "string" ? "" : requestOrId.conversationId;
    prompted.current.add(id);
    let ids = [id];
    if (conversationId) {
      try {
        const page = await api.questions({ status: "open", scope: "tenant", conversationId, pageSize: 25 });
        ids = [id, ...page.items.map(item => item.id).filter(item => item !== id)];
      } catch { /* open the one request */ }
    }
    setToast(null); setQueue(ids); setIndex(0);
  }, [api]);

  useEffect(() => {
    let stopped = false;
    let leaving = false;
    const controller = new AbortController();
    const leave = () => { leaving = true; controller.abort(); };
    window.addEventListener("pagehide", leave);
    const onWorkspace = conversationId => path.current === "/dashboard/conversations/" + conversationId;
    const handle = async change => {
      setRevision(value => value + 1);
      await refresh();
      if (change.status !== "opened" || prompted.current.has(change.requestId)) return;
      const request = await api.question(change.requestId).catch(() => null);
      if (!request || request.status !== "open" || !request.wait) return;
      if (onWorkspace(request.conversationId)) { open(request); return; }
      setToast(request);
      if (notificationsEnabled() && document.hidden && "Notification" in window && Notification.permission === "granted") {
        const notice = new Notification(t("questions.notification.title"), { body: t("questions.notification.body", { name: request.askerName || request.askerId }), tag: request.id });
        notice.onclick = () => { window.focus(); open(request); notice.close(); };
      }
    };
    const pause = milliseconds => new Promise(resolve => { const timer = window.setTimeout(resolve, milliseconds); controller.signal.addEventListener("abort", () => { window.clearTimeout(timer); resolve(); }, { once: true }); });
    // Read the change feed once; the cursor makes later reads report only changes after this one.
    const poll = async () => {
      const page = await api.questionEvents(cursor.current, 100, controller.signal);
      cursor.current = page.next;
      for (const change of page.items) await handle(change);
    };
    const watch = async () => {
      try { await poll(); setLive(true); } catch { setLive(false); }
      while (!stopped) {
        await pause(POLL_MILLISECONDS);
        // A hidden tab, or a page being left, does not poll; the next visible poll catches up from the cursor.
        if (stopped || leaving) return;
        if (document.hidden) continue;
        try { await poll(); setLive(true); } catch { setLive(false); }
      }
    };
    refresh();
    watch();
    return () => { stopped = true; controller.abort(); window.removeEventListener("pagehide", leave); };
  }, [api, open, refresh, t]);

  // A conversation opened while its agent is already waiting shows the question at once.
  useEffect(() => {
    const match = location.pathname.match(/^\/dashboard\/conversations\/([^/]+)$/);
    if (!match || !summary?.openTenant) return;
    let active = true;
    api.questions({ status: "open", scope: "tenant", conversationId: decodeURIComponent(match[1]), pageSize: 25 }).then(page => {
      const waiting = page.items.find(item => item.wait && !prompted.current.has(item.id));
      if (active && waiting) open(waiting);
    }).catch(() => {});
    return () => { active = false; };
  }, [api, location.pathname, open, summary?.openTenant]);

  useEffect(() => {
    const count = summary?.openMine || 0;
    document.title = count > 0 ? `(${count}) ${baseTitle.current}` : baseTitle.current;
  }, [summary?.openMine]);

  const close = () => { setQueue([]); setIndex(0); refresh(); };
  const value = useMemo(() => ({ available: true, summary, open, refresh, revision, live }), [summary, open, refresh, revision, live]);
  return <QuestionContext.Provider value={value}>
    {children}
    {toast && queue.length === 0 && <div className="toast question-toast" role="status">
      <span>{t("questions.toast", { name: toast.askerName || toast.askerId, title: toast.conversationTitle })}</span>
      <Button label="questions.answer" className="primary" onClick={() => open(toast)} />
      <Button label="questions.goTo" onClick={() => { setToast(null); navigate("/dashboard/conversations/" + encodeURIComponent(toast.conversationId)); }} />
      <Button label="common.dismiss" icon="close" iconOnly onClick={() => setToast(null)} />
    </div>}
    {queue.length > 0 && <QuestionModal api={api} requestId={queue[index]} position={index + 1} count={queue.length}
      onPrevious={() => setIndex(value => Math.max(0, value - 1))} onNext={() => setIndex(value => Math.min(queue.length - 1, value + 1))}
      onClose={close} onResolved={() => { setRevision(value => value + 1); refresh(); }} />}
  </QuestionContext.Provider>;
}
