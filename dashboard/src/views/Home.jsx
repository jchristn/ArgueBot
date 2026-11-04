import { useState } from "react";
import { Link, useNavigate, useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Icon } from "../components/ui.jsx";
import ConversationForm from "../components/ConversationForm.jsx";
import { formatDate } from "../i18n/index.js";
export default function Home() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const { data, error, reload } = useData("home", async () => {
    const [conversations, participants] = await Promise.all([
      api.conversations(),
      api.participants(),
    ]);
    return { conversations, participants };
  });
  // Latest run state for the visible recent conversations loads separately, so the page does not wait on it.
  const recentIds = data?.conversations.items.slice(0, 8).map((conversation) => conversation.id) ?? [];
  const { data: runs } = useData("home-runs:" + recentIds.join(","), async () => {
    const latest = await Promise.all(recentIds.map(async (conversationId) => {
      try { return (await api.runs(conversationId)).at(-1) || null; } catch { return null; }
    }));
    return Object.fromEntries(recentIds.map((conversationId, index) => [conversationId, latest[index]]));
  });
  const status = (run) => {
    if (!run) return null;
    if (["paused", "failed"].includes(run.state)) return { tone: "attention", label: t("home.run.attention") };
    if (!["completed", "cancelled"].includes(run.state)) return { tone: "active", label: t("home.run.active", { count: run.participants?.length ?? 0 }) };
    if (run.outcome === "consensus" && run.protocolVersion === 2) return { tone: "done", label: t("home.run.unanimous") };
    return { tone: "done", label: t(`state.${run.state === "completed" ? run.outcome || "completed" : run.state}`, { defaultValue: run.state }) };
  };
  const attention = runs ? Object.values(runs).filter((run) => run && ["paused", "failed"].includes(run.state)).length : null;
  return (
    <>
      <div className="page-heading">
        <div>
          <p className="eyebrow">{t("app.name")}</p>
          <h1>{t("home.title")}</h1>
          <p>{t("home.description")}</p>
        </div>
        <Button
          label="home.new"
          icon="plus"
          className="primary"
          onClick={() => setCreating(true)}
        />
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      <div className="metrics">
        <article>
          <span>{t("home.conversations")}</span>
          <strong>{data?.conversations.totalCount ?? "…"}</strong>
        </article>
        <article>
          <span>{t("home.participants")}</span>
          <strong>{data?.participants.length ?? "…"}</strong>
        </article>
        <article>
          <span>{t("home.attention")}</span>
          <strong>{attention ?? "…"}</strong>
        </article>
        <article>
          <span>{t("home.local")}</span>
          <strong className="metric-status">
            <span className="status-dot" />
            {t("common.connected")}
          </strong>
        </article>
      </div>
      <section className="page-section">
        <header>
          <h2>{t("home.recent")}</h2>
          <Link
            to="/dashboard/participants"
            title={t("home.configuration.help")}
          >
            {t("home.configuration")} <Icon name="arrow" />
          </Link>
        </header>
        {data?.conversations.items.length === 0 ? (
          <div className="empty-state">
            <Icon name="chat" />
            <h3>{t("home.empty")}</h3>
            <p>{t("home.empty.help")}</p>
            <Button
              label="home.new"
              icon="plus"
              onClick={() => setCreating(true)}
            />
          </div>
        ) : (
          <div className="recent-list">
            {data?.conversations.items.slice(0, 8).map((conversation) => (
              <Link
                key={conversation.id}
                to={"/dashboard/conversations/" + conversation.id}
                title={t("conversations.open.help")}
              >
                <span>{conversation.title}</span>
                {status(runs?.[conversation.id]) && <span className={"badge run-status " + status(runs[conversation.id]).tone}>{status(runs[conversation.id]).label}</span>}
                <time>{formatDate(conversation.updatedUtc)}</time>
                <Icon name="arrow" />
              </Link>
            ))}
          </div>
        )}
      </section>
      <p className="product-note">{t("home.notice")}</p>
      {creating && (
        <ConversationForm
          onClose={() => setCreating(false)}
          onSave={async (title) => {
            try {
              const record = await api.createConversation(title);
              setCreating(false);
              navigate("/dashboard/conversations/" + record.id);
            } catch (failure) {
              notify(failure);
            }
          }}
        />
      )}
    </>
  );
}
