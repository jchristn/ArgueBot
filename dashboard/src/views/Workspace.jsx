import { useEffect, useRef, useState } from "react";
import { useOutletContext, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";
function StartDialog({ profiles, onStart, onClose }) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState([]);
  const [rounds, setRounds] = useState(5);
  const [seconds, setSeconds] = useState(10);
  const [busy, setBusy] = useState(false);
  return (
    <Modal title="debate.new" onClose={onClose}>
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);
          try {
            await onStart({
              participantIds: selected,
              maxRounds: rounds,
              interventionSeconds: seconds,
            });
          } finally {
            setBusy(false);
          }
        }}
      >
        <fieldset title={t("debate.participants.help")}>
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
                    !selected.includes(profile.id) && selected.length >= 3
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
            disabled={busy || selected.length < 2}
          />
        </footer>
      </form>
    </Modal>
  );
}
function WorkspaceContent({ id }) {
  const { api, notify, user } = useOutletContext();
  const { t } = useTranslation();
  const draftKey = `arguebot.draft:${location.origin}:${user.userId}:${id}`;
  const [text, setText] = useState(() => localStorage.getItem(draftKey) || "");
  const [mode, setMode] = useState("note");
  const [recipient, setRecipient] = useState("");
  const [starting, setStarting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [connection, setConnection] = useState("common.connected");
  const feed = useRef();
  const tail = useRef(true);
  const { data, error, reload } = useData(id, async () => {
    const [conversation, messages, runs, profiles] = await Promise.all([
      api.conversation(id),
      api.messages(id),
      api.runs(id),
      api.participants(),
    ]);
    return { conversation, messages, runs, profiles };
  });
  const run = data?.runs.at(-1);
  const active =
    run && !["completed", "failed", "cancelled"].includes(run.state);
  useEffect(() => {
    localStorage.setItem(draftKey, text);
  }, [draftKey, text]);
  useEffect(() => {
    if (tail.current && feed.current)
      feed.current.scrollTop = feed.current.scrollHeight;
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
    const watch = async () => {
      while (!controller.signal.aborted) {
        try {
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
  const send = async (event) => {
    event?.preventDefault();
    if (
      !text.trim() ||
      busy ||
      (mode === "steer" && (!active || run?.mode === "followup"))
    )
      return;
    setBusy(true);
    try {
      if (mode === "steer" && active)
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
      setText("");
      await reload();
    } catch (failure) {
      notify(failure);
    } finally {
      setBusy(false);
    }
  };
  const download = () => {
    const content =
      "# " +
      data.conversation.title +
      "\n\n" +
      data.messages
        .map(
          (message) =>
            `## ${message.speaker} (${message.kind})\n\n${message.content}`,
        )
        .join("\n\n");
    const url = URL.createObjectURL(
      new Blob([content], { type: "text/markdown;charset=utf-8" }),
    );
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = id + ".md";
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  if (error && !data) return <ErrorPanel error={error} retry={reload} />;
  if (!data) return <p>{t("common.loading")}</p>;
  return (
    <div className="workspace">
      <header className="workspace-heading">
        <div>
          <p className="eyebrow">{t("debate.title")}</p>
          <h1 title={data.conversation.title}>{data.conversation.title}</h1>
          <span className="live-label">
            <span className="status-dot" />
            {t(connection)}
          </span>
        </div>
        <div className="utilities">
          <Button
            label="conversations.export"
            icon="download"
            iconOnly
            disabled={!data.messages.length}
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
            <div className="run-controls">
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
      <div
        className="message-feed"
        ref={feed}
        onScroll={() => {
          if (feed.current)
            tail.current =
              feed.current.scrollHeight -
                feed.current.scrollTop -
                feed.current.clientHeight <
              80;
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
        {data.messages.map((message) => (
          <article
            className={
              "message " + (message.speaker === "user" ? "user-message" : "")
            }
            key={message.id}
          >
            <header>
              <strong title={message.speaker}>{message.speaker}</strong>
              <span className="badge" title={t("composer.mode.help")}>
                {t("state." + message.kind, { defaultValue: message.kind })}
              </span>
              <time title={formatDate(message.createdUtc)}>
                {formatDate(message.createdUtc)}
              </time>
              {message.state === "streaming" && (
                <span className="stream-dot" title={t("state.streaming")} />
              )}
            </header>
            <div className="markdown">
              <Markdown remarkPlugins={[remarkGfm]}>{message.content}</Markdown>
            </div>
            {message.state === "interrupted" && (
              <small className="warning">{t("debate.partial")}</small>
            )}
          </article>
        ))}
      </div>
      <form className="composer" onSubmit={send}>
        <div className="composer-options">
          <select
            aria-label={t("composer.mode")}
            title={t("composer.mode.help")}
            value={mode}
            onChange={(event) => {
              setMode(event.target.value);
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
              onChange={(event) => setRecipient(event.target.value)}
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
          <span>{t("composer.draft")}</span>
        </div>
        <div className="composer-input">
          <textarea
            aria-label={t("composer.label")}
            title={t("composer.label.help")}
            placeholder={t("composer.placeholder")}
            rows="2"
            value={text}
            onChange={(event) => setText(event.target.value)}
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
          onClose={() => setStarting(false)}
          onStart={async (request) => {
            try {
              await api.startRun(id, request);
              setStarting(false);
              await reload();
            } catch (failure) {
              notify(failure);
            }
          }}
        />
      )}
    </div>
  );
}
export default function Workspace() {
  const { id } = useParams();
  return <WorkspaceContent key={id} id={id} />;
}
