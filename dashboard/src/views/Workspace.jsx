import { mergeToolActivity, messageContent } from "../utils/message-content.js";
import DebatePolicyEditor from "../components/DebatePolicyEditor.jsx";
import DebatePanel from "../components/DebatePanel.jsx";
import { randomId } from "../utils/random-id.js";
import RunActivity from "../components/RunActivity.jsx";
import FailedTurn from "../components/FailedTurn.jsx";
import CopyButton from "../components/CopyButton.jsx";
import CopyableId from "../components/CopyableId.jsx";
import { useEffect, useRef, useState } from "react";
import { useOutletContext, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Icon, Modal, Pagination, speakerLabel } from "../components/ui.jsx";
import FullMessageDialog from "../components/FullMessageDialog.jsx";
import AttemptDialog from "../components/AttemptDialog.jsx";
import { formatDate } from "../i18n/index.js";
function StartDialog({ profiles, maximum, capabilities, onStart, onClose, notify, onRefresh }) {
  const { t } = useTranslation();
  const [fieldError, setFieldError] = useState(null);
  const [removedNotice, setRemovedNotice] = useState(false);
  const [selected, setSelected] = useState([]);
  const [orchestrator, setOrchestrator] = useState("");
  const [policy, setPolicy] = useState(() => structuredClone(capabilities.defaultPolicy));
  const [commandKey, setCommandKey] = useState(randomId);
  const [rounds, setRounds] = useState(5);
  const [seconds, setSeconds] = useState(10);
  const [busy, setBusy] = useState(false);
  useEffect(() => setCommandKey(randomId()), [selected, orchestrator, rounds, seconds, policy]);
  useEffect(() => {
    const available = new Set(profiles.filter(profile => profile.enabled).map(profile => profile.id));
    setSelected(value => { const kept = value.filter(id => available.has(id)); if (kept.length === value.length) return value; setRemovedNotice(true); return kept; });
    setOrchestrator(value => { if (!value || available.has(value)) return value; setRemovedNotice(true); return ""; });
  }, [profiles]);
  const selectedNames = selected.map(id => profiles.find(profile => profile.id === id)?.name || "");
  const duplicateName = selectedNames.find((name, index) => name && selectedNames.findIndex(other => other.toLocaleLowerCase() === name.toLocaleLowerCase()) !== index);
  const errorFor = field => fieldError && (fieldError.field === field || (field === "policy" && fieldError.field.startsWith("policy."))) ? fieldError.message : "";
  return (
    <Modal title="debate.new" onClose={onClose}>
      <form
        onChange={() => { setFieldError(null); setRemovedNotice(false); }}
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);
          setFieldError(null);
          try {
            await onStart({
              protocolVersion: 2,
              orchestratorId: orchestrator,
              idempotencyKey: commandKey,
              participantIds: selected,
              maxRounds: rounds,
              interventionSeconds: seconds,
              policy,
            });
          } catch (failure) {
            if (failure.code === "profile_not_found" || failure.code === "disabled_profile") await onRefresh();
            if (failure.field) setFieldError({ field: failure.field, message: failure.message });
            else notify(failure);
          } finally {
            setBusy(false);
          }
        }}
      >
        {fieldError && <p className="field-error" role="alert">{t("debate.fieldError")}</p>}
        <fieldset title={t("debate.participants.help")} aria-invalid={errorFor("participantIds") || duplicateName ? "true" : undefined} className={errorFor("participantIds") || duplicateName ? "invalid" : undefined}>
          <legend>{t("debate.participants")}</legend>
          <p className="muted">{t("debate.participants.help")}</p>
          {profiles
            .filter((profile) => profile.enabled)
            .map((profile) => (
              <label
                key={profile.id}
                className="participant-choice"
                title={t("debate.participants.help")}
              >
                <input
                  type="checkbox"
                  title={t("debate.participants.help")}
                  checked={selected.includes(profile.id)}
                  disabled={
                    !selected.includes(profile.id) && selected.length >= maximum
                  }
                  onChange={(event) =>
                    setSelected((value) =>
                      event.target.checked
                        ? [...value, profile.id]
                        : value.filter((id) => id !== profile.id),
                    )
                  }
                />
                <span>{profile.name}</span>
                <small>
                  {profile.kind} · {profile.model || profile.provider}
                </small>
                {selected.includes(profile.id) && (
                  <b>{selected.indexOf(profile.id) + 1}</b>
                )}
              </label>
            ))}
        </fieldset>
        {duplicateName && <p className="field-error" role="alert">{t("debate.duplicateNames", { name: duplicateName })}</p>}
        {errorFor("participantIds") && <p className="field-error" role="alert">{errorFor("participantIds")}</p>}
        {removedNotice && <p className="notice" role="status">{t("debate.removedSelection")}</p>}
        {selected.length > 0 && <ol className="selected-panel">{selected.map((id, index) => <li key={id}><span>{profiles.find(profile => profile.id === id)?.name}</span><button type="button" className="button" disabled={index === 0} aria-label={t("panel.moveEarlier", { name: profiles.find(profile => profile.id === id)?.name })} onClick={() => setSelected(value => { const next = [...value]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; return next; })}>↑</button><button type="button" className="button" disabled={index === selected.length - 1} aria-label={t("panel.moveLater", { name: profiles.find(profile => profile.id === id)?.name })} onClick={() => setSelected(value => { const next = [...value]; [next[index + 1], next[index]] = [next[index], next[index + 1]]; return next; })}>↓</button></li>)}</ol>}
        <Field label="panel.orchestrator">{props => <select {...props} required className={errorFor("orchestratorId") ? "invalid" : undefined} aria-invalid={errorFor("orchestratorId") ? "true" : undefined} value={orchestrator} onChange={event => setOrchestrator(event.target.value)}><option value="">{t("panel.choose")}</option>{profiles.filter(profile => profile.enabled).map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}</select>}</Field>
        {errorFor("orchestratorId") && <p className="field-error" role="alert">{errorFor("orchestratorId")}</p>}
        <p className="muted">{t("panel.setupHelp", { maximum })}</p>
        <div className="form-grid">
          <Field label="debate.rounds">
            {(props) => (
              <input
                {...props}
                type="number"
                min="1"
                max="50"
                value={rounds}
                onChange={(event) => setRounds(Number(event.target.value))}
              />
            )}
          </Field>
          <Field label="debate.seconds">
            {(props) => (
              <input
                {...props}
                type="number"
                min="0"
                max="600"
                value={seconds}
                onChange={(event) => setSeconds(Number(event.target.value))}
              />
            )}
          </Field>
        </div>
        {["maxRounds", "interventionSeconds"].map(field => errorFor(field) && <p key={field} className="field-error" role="alert">{errorFor(field)}</p>)}
        <DebatePolicyEditor value={policy} maximum={capabilities.maximumPolicy} onChange={setPolicy} />
        {errorFor("policy") && <p className="field-error" role="alert">{errorFor("policy")}</p>}
        <p className="muted">{t("policy.workload", { turns: selected.length * rounds, orchestration: rounds * 2 })}</p>
        <p className="muted">{t("debate.context")}</p>
        <footer>
          <Button
            label="common.cancel"
            help="common.cancel"
            type="button"
            onClick={onClose}
          />
          <Button
            label="debate.start"
            className="primary"
            type="submit"
            disabled={busy || selected.length < 2 || !orchestrator || !!duplicateName}
          />
        </footer>
      </form>
    </Modal>
  );
}
function WorkspaceContent({ id }) {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const draftKey = `arguebot.draft:${location.origin}:${id}`;
  const [text, setText] = useState(() => localStorage.getItem(draftKey) || "");
  const [mode, setMode] = useState("note");
  const [deliveryMode, setDeliveryMode] = useState(() => localStorage.getItem(draftKey + ":delivery") || "");
  const [contextKey, setContextKey] = useState(randomId);
  const [pinned, setPinned] = useState(false);
  const [recipient, setRecipient] = useState("");
  const [starting, setStarting] = useState(false);
  const [attemptsRunId, setAttemptsRunId] = useState(null);
  const [fullMessage, setFullMessage] = useState(null);
  const [collapsed, setCollapsed] = useState(() => new Set());
  const [jumpTarget, setJumpTarget] = useState("");
  const toggleMessage = messageId => setCollapsed(current => { const next = new Set(current); if (next.has(messageId)) next.delete(messageId); else next.add(messageId); return next; });
  const jumpTo = messageId => {
    if (!messageId) return;
    setCollapsed(current => { if (!current.has(messageId)) return current; const next = new Set(current); next.delete(messageId); return next; });
    tail.current = false;
    setJumpTarget(messageId);
    requestAnimationFrame(() => { const target = document.getElementById("message-" + messageId); target?.scrollIntoView({ block: "start", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" }); target?.querySelector(".message-toggle")?.focus({ preventScroll: true }); });
    setTimeout(() => setJumpTarget(current => current === messageId ? "" : current), 1600);
  };
  const [historyView, setHistoryView] = useState({ pageNumber: 1, pageSize: 25, tail: true });
  const [panelOpen, setPanelOpen] = useState(() => { const saved = localStorage.getItem("arguebot.debatePanel"); return saved ? saved === "open" : window.innerWidth >= 1200; });
  const togglePanel = () => setPanelOpen(current => { localStorage.setItem("arguebot.debatePanel", current ? "closed" : "open"); return !current; });
  const [busy, setBusy] = useState(false);
  const [connection, setConnection] = useState("common.connected");
  const feed = useRef();
  const tail = useRef(true);
  const scrollPageTop = useRef(false);
  const { data, error, reload } = useData(id + JSON.stringify(historyView), async () => {
    const [conversation, messagePage, runs, profiles, capabilities] = await Promise.all([
      api.conversation(id),
      api.messages(id, historyView),
      api.runs(id),
      api.participants(),
      api.capabilities(),
    ]);
    return { conversation, messages: messagePage.items, messagePage, runs, profiles, capabilities };
  });
  const run = data?.runs.at(-1);
  const active =
    run && !["completed", "failed", "cancelled"].includes(run.state);
  useEffect(() => {
    localStorage.setItem(draftKey, text);
    localStorage.setItem(draftKey + ":delivery", deliveryMode);
  }, [draftKey, text, deliveryMode]);
  useEffect(() => {
    if (scrollPageTop.current && feed.current) { feed.current.scrollTop = 0; scrollPageTop.current = false; }
    else if (tail.current && feed.current) feed.current.scrollTop = feed.current.scrollHeight;
  }, [data?.messages]);
  useEffect(() => {
    const controller = new AbortController();
    let cursor = "";
    let timer;
    let fallback;
    const queueReload = () => {
      clearTimeout(timer);
      timer = setTimeout(reload, 100);
    };
    const bootstrap = async () => {
      const head = await api.conversation(id);
      cursor = id + ":" + head.lastEventSequence;
      await reload();
    };
    const watch = async () => {
      let needsSnapshot = true;
      while (!controller.signal.aborted) {
        try {
          if (needsSnapshot) { await bootstrap(); needsSnapshot = false; }
          setConnection("common.connected");
          await api.events(
            id,
            cursor,
            (_, eventId) => {
              cursor = eventId;
              queueReload();
            },
            controller.signal,
          );
        } catch (failure) {
          if (controller.signal.aborted) break;
          setConnection("common.reconnecting");
          if (["event_history_gap", "invalid_cursor", "invalid_event", "event_frame_too_large"].includes(failure.code)) needsSnapshot = true;
          if (failure.status === 401) {
            notify(failure);
            break;
          }
        }
        if (!controller.signal.aborted)
          await new Promise((resolve) => setTimeout(resolve, 1000));
      }
    };
    watch();
    fallback = setInterval(reload, 3000);
    return () => {
      controller.abort();
      clearTimeout(timer);
      clearInterval(fallback);
    };
  }, [id, api, reload, notify]);
  const control = async (action) => {
    if (!run || busy) return;
    setBusy(true);
    try {
      await api.action(id, run.id, {
        action,
        revision: run.revision,
        rounds: 2,
      });
      await reload();
    } catch (failure) {
      notify(failure);
    } finally {
      setBusy(false);
    }
  };
  const needsDelivery = run?.protocolVersion === 2 && mode !== "followup";
  const send = async (event) => {
    event?.preventDefault();
    if (
      !text.trim() ||
      busy ||
      (needsDelivery && !deliveryMode) ||
      (mode === "steer" && (!active || run?.mode === "followup"))
    )
      return;
    setBusy(true);
    try {
      if (mode !== "followup" && run?.protocolVersion === 2)
        await api.interject(id, { runId: run.id, text, deliveryMode, pinned, recipientId: mode === "steer" ? recipient || run.participants[run.speakerIndex]?.id : "", idempotencyKey: contextKey });
      else if (mode === "steer" && active)
        await api.action(id, run.id, {
          action: "steer",
          text,
          recipientId: recipient || run.participants[run.speakerIndex]?.id,
        });
      else if (mode === "followup")
        await api.startRun(id, {
          mode: "followup",
          question: text,
          participantIds: [recipient || data.profiles[0]?.id],
          maxRounds: 1,
          interventionSeconds: 0,
        });
      else await api.note(id, text);
      setText(""); setDeliveryMode(""); setContextKey(randomId());
      await reload();
    } catch (failure) {
      notify(failure);
    } finally {
      setBusy(false);
    }
  };
  const download = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const messages = await api.allMessages(id);
      const content = "# " + data.conversation.title + "\n\n" + messages.map((message) => `## ${message.speaker} (${message.kind})\n\n${messageContent(message)}`).join("\n\n");
      const url = URL.createObjectURL(new Blob([content], { type: "text/markdown;charset=utf-8" }));
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = id + ".md"; anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (failure) { notify(failure); }
    finally { setBusy(false); }
  };
  const latest = () => { scrollPageTop.current = false; tail.current = true; setHistoryView((current) => ({ pageNumber: 1, pageSize: current.pageSize, tail: true })); };
  if (error && !data) return <ErrorPanel error={error} retry={reload} />;
  if (!data) return <p>{t("common.loading")}</p>;
  return (
    <div className="workspace">
      {fullMessage && <FullMessageDialog conversationId={id} messageId={fullMessage} onClose={() => setFullMessage(null)} />}
      {attemptsRunId && <AttemptDialog conversationId={id} runs={data.runs} messages={data.messages} initialRunId={attemptsRunId} onClose={() => setAttemptsRunId(null)} />}
      <header className="workspace-heading">
        <div>
          <p className="eyebrow">{t("debate.title")}</p>
          <h1 title={data.conversation.title}>{data.conversation.title}</h1>
          <CopyableId value={id} />
          <span className="live-label">
            <span className="status-dot" />
            {t(connection)}
          </span>
        </div>
        <div className="utilities">
          <CopyButton label="conversations.copy" disabled={!data.messagePage.totalCount} loadText={async () => {
            const messages = await api.allMessages(id);
            return "# " + data.conversation.title + "\n\n" + messages.map(message => `## ${message.speaker} (${message.kind})\n\n${messageContent(message)}`).join("\n\n");
          }} />
          <Button
            label="conversations.export"
            icon="download"
            iconOnly
            disabled={!data.messagePage.totalCount || busy}
            onClick={download}
          />
          <Button
            label="debate.new"
            icon="plus"
            iconOnly
            disabled={
              !!active ||
              data.profiles.filter((profile) => profile.enabled).length < 2
            }
            onClick={() => setStarting(true)}
          />
        </div>
      </header>
      <div className="run-strip">
        {run ? (
          <>
            <CopyableId value={run.id} />
            <span
              className={"badge state-" + run.state}
              title={t("debate.status.help")}
            >
              {t("state." + run.state, { defaultValue: run.state })}
            </span>
            <span title={t("debate.rounds.help")}>
              {t("debate.round", {
                round: Math.min(run.round, run.maxRounds),
                max: run.maxRounds,
              })}
            </span>
            <span
              className="run-participants"
              title={t("debate.participants.help")}
            >
              {run.participants.map((profile) => profile.name).join(" · ")}
            </span>
            {run.outcome && <span className="muted" role="status" title={t("debate.outcome.help")}>{t(run.protocolVersion === 2 && ["consensus", "max_rounds"].includes(run.outcome) ? `outcome.v2.${run.outcome}` : `outcome.${run.outcome}`, { defaultValue: run.outcome })}</span>}
            <div className="run-controls">
              {run.protocolVersion === 2 && <Button label={panelOpen ? "panel.hide" : "panel.show"} aria-expanded={panelOpen} aria-controls="debate-panel" onClick={togglePanel} />}
              <Button label="attempts.title" onClick={() => setAttemptsRunId(run.id)} />
              {active && (
                <>
                  {run.state === "paused" ? (
                    <Button
                      label="debate.resume"
                      disabled={busy}
                      onClick={() => control("resume")}
                    />
                  ) : (
                    <Button
                      label="debate.pause"
                      disabled={busy}
                      onClick={() => control("pause")}
                    />
                  )}
                  <Button
                    label="debate.interrupt"
                    disabled={busy || run.state !== "agent_turn"}
                    onClick={() => control("interrupt")}
                  />
                  <Button
                    label="debate.stop"
                    className="danger"
                    disabled={busy}
                    onClick={() => control("stop")}
                  />
                </>
              )}
              {run.mode !== "followup" && (
                <Button
                  label="debate.extend"
                  disabled={
                    busy ||
                    run.maxRounds > 48 ||
                    (!active && run.outcome !== "max_rounds")
                  }
                  onClick={() => control("extend")}
                />
              )}
            </div>
          </>
        ) : (
          <span>{t("debate.noRuns")}</span>
        )}
      </div>
      <RunActivity run={run} messages={data.messages} connection={connection} />
      <div className="history-navigation">
        <Pagination page={data.messagePage} onChange={(changes) => { scrollPageTop.current = true; tail.current = false; setHistoryView((current) => ({ ...current, ...changes, tail: false, throughSequence: data.messagePage.throughSequence })); }} />
        {(!historyView.tail || data.messagePage.latestSequence > data.messagePage.throughSequence) && <Button label={data.messagePage.latestSequence > data.messagePage.throughSequence ? "messages.newer" : "messages.latest"} onClick={latest} />}
      </div>
      {data.messages.length > 0 && <div className="feed-toolbar" role="toolbar" aria-label={t("messages.navigation")}>
        <Button label="messages.collapseAll" onClick={() => setCollapsed(new Set(data.messages.map(message => message.id)))} />
        <Button label="messages.expandAll" disabled={collapsed.size === 0} onClick={() => setCollapsed(new Set())} />
        <select aria-label={t("messages.jump")} title={t("messages.jump.help")} value="" onChange={event => jumpTo(event.target.value)}>
          <option value="">{t("messages.jump")}</option>
          {mergeToolActivity(data.messages).map((message, index) => <option key={message.id} value={message.id}>{`${index + 1}. ${speakerLabel(t, message.speaker)} · ${t("state." + message.kind, { defaultValue: message.kind })}`}</option>)}
        </select>
        <span className="feed-count">{t("messages.shown", { count: mergeToolActivity(data.messages).length })}</span>
      </div>}
      <div className={"workspace-body" + (run?.protocolVersion === 2 && panelOpen ? " with-panel" : "")}>
      <div
        className="message-feed"
        ref={feed}
        onScroll={() => {
          if (feed.current) {
            tail.current = feed.current.scrollHeight - feed.current.scrollTop - feed.current.clientHeight < 80;
            if (!tail.current && historyView.tail) setHistoryView((current) => ({ ...current, tail: false, pageNumber: data.messagePage.pageNumber, throughSequence: data.messagePage.throughSequence }));
          }
        }}
        aria-label={t("debate.history")}
      >
        {data.messages.length === 0 && (
          <div className="empty-state">
            <p>{t("home.empty.help")}</p>
            <Button
              label="debate.start"
              className="primary"
              disabled={data.profiles.length < 2}
              onClick={() => setStarting(true)}
            />
          </div>
        )}
        {mergeToolActivity(data.messages).map((message) => {
          const folded = collapsed.has(message.id);
          return <article
            id={"message-" + message.id}
            className={"message" + (message.speaker === "user" ? " user-message" : "") + (folded ? " collapsed" : "") + (jumpTarget === message.id ? " jump-target" : "")}
            key={message.id}
          >
            <header>
              <button type="button" className="message-toggle" aria-expanded={!folded} aria-controls={"message-body-" + message.id} aria-label={t(folded ? "messages.expand" : "messages.collapse", { speaker: speakerLabel(t, message.speaker) })} title={t(folded ? "messages.expand" : "messages.collapse", { speaker: speakerLabel(t, message.speaker) })} onClick={() => toggleMessage(message.id)}><Icon name="chevron" /></button>
              <strong title={speakerLabel(t, message.speaker)}>{speakerLabel(t, message.speaker)}</strong>
              <span className="badge" title={t("composer.mode.help")}>
                {t("state." + (message.kind === "tool" && message.content.startsWith("Conversation tool:") ? "contextTool" : message.kind === "question" && message.speaker !== "user" ? "orchestratorQuestion" : message.kind), { defaultValue: message.kind })}
              </span>
              <time title={formatDate(message.createdUtc)}>
                {formatDate(message.createdUtc)}
              </time>
              {message.state === "streaming" && (
                <span className="stream-dot" title={t("state.streaming")} />
              )}
              <CopyableId value={message.id} />
              <CopyButton label="messages.copy" loadText={async () => (await api.message(id, message.id)).content} />
              {message.contentTruncated && <Button label="messages.full" onClick={() => setFullMessage(message.id)} />}
            </header>
            {folded ? <p className="message-preview" onClick={() => toggleMessage(message.id)}>{previewText(messageContent(message))}</p> : <div id={"message-body-" + message.id}>
            {message.contentTruncated && <p className="muted" title={t("messages.preview.help")}>{t("messages.preview", { bytes: message.contentBytes })}</p>}
            {message.kind === "tool" ? <details className="tool-evidence"><summary>{message.content.split("\n").slice(0, 2).join(" · ")}</summary><pre>{message.content}</pre></details> : <div className="markdown">
              <Markdown remarkPlugins={[remarkGfm]}>{messageContent(message)}</Markdown>
            </div>}
            {["failed", "interrupted"].includes(message.state) && <FailedTurn api={api} conversationId={id} message={message} onInspect={() => setAttemptsRunId(message.runId)} />}
            </div>}
          </article>;
        })}
      </div>
      {run?.protocolVersion === 2 && panelOpen && <aside className="debate-sidebar" aria-label={t("panel.title")}><DebatePanel api={api} id={id} run={run} profiles={data.profiles} notify={notify} onChange={reload} /></aside>}
      </div>
      <form className="composer" onSubmit={send}>
        <div className="composer-options">
          <select
            aria-label={t("composer.mode")}
            title={t("composer.mode.help")}
            value={mode}
            onChange={(event) => {
              setMode(event.target.value); setContextKey(randomId());
              setRecipient("");
            }}
          >
            <option value="note">{t("composer.note")}</option>
            <option
              value="steer"
              disabled={!active || run?.mode === "followup"}
            >
              {t("composer.steer")}
            </option>
            <option value="followup" disabled={!!active}>
              {t("composer.followup")}
            </option>
          </select>
          {mode !== "note" && (
            <select
              aria-label={t("composer.recipient")}
              title={t("composer.recipient.help")}
              value={
                recipient ||
                (mode === "steer"
                  ? run?.participants[run.speakerIndex]?.id
                  : data.profiles[0]?.id) ||
                ""
              }
              onChange={(event) => { setRecipient(event.target.value); setContextKey(randomId()); }}
            >
              {(mode === "steer" ? run?.participants || [] : data.profiles).map(
                (profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}
                  </option>
                ),
              )}
            </select>
          )}
          {run?.protocolVersion === 2 && mode !== "followup" && <label className="checkbox-label"><input type="checkbox" checked={pinned} onChange={event => { setPinned(event.target.checked); setContextKey(randomId()); }} />{t("panel.pin")}</label>}
          {run?.protocolVersion === 2 && mode !== "followup" && <select aria-label={t("panel.delivery")} className={deliveryMode ? "" : "needs-choice"} required value={deliveryMode} onChange={event => { setDeliveryMode(event.target.value); setContextKey(randomId()); }}><option value="" disabled>{t("delivery.choose")}</option><option value="next_turn">{t("delivery.next_turn")}</option><option value="interrupt_now">{t("delivery.interrupt_now")}</option></select>}
          <span>{t("composer.draft")}</span>
        </div>
        <div className="composer-input">
          <textarea
            aria-label={t("composer.label")}
            title={t("composer.label.help")}
            placeholder={t("composer.placeholder")}
            rows="2"
            value={text}
            onChange={(event) => { setText(event.target.value); setContextKey(randomId()); }}
            onKeyDown={(event) => {
              if (
                event.key === "Enter" &&
                !event.shiftKey &&
                !event.nativeEvent.isComposing
              ) {
                event.preventDefault();
                send();
              }
            }}
          />
          <Button
            label="composer.send"
            className="primary"
            type="submit"
            disabled={
              busy ||
              !text.trim() ||
              (needsDelivery && !deliveryMode) ||
              (mode === "followup" && (!!active || !data.profiles.length)) ||
              (mode === "steer" && (!active || run?.mode === "followup"))
            }
          />
        </div>
        <small>{t("composer.hint")}</small>
      </form>
      {starting && (
        <StartDialog
          profiles={data.profiles}
          maximum={data.capabilities.maxParticipants}
          capabilities={data.capabilities}
          onClose={() => setStarting(false)}
          notify={notify}
          onRefresh={reload}
          onStart={async (request) => {
            await api.startRun(id, request);
            setStarting(false);
            await reload();
          }}
        />
      )}
    </div>
  );
}
/** One line of plain text from a message, for its collapsed preview. */
function previewText(content) {
  return (content || "").replace(/```[\s\S]*?```/g, " ").replace(/[#*_>`|[\]()-]+/g, " ").replace(/\s+/g, " ").trim().slice(0, 220);
}
export default function Workspace() {
  const { id } = useParams();
  return <WorkspaceContent key={id} id={id} />;
}
