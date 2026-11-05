import { useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { api } from "../api/client.js";
import CopyButton from "../components/CopyButton.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Icon, speakerLabel } from "../components/ui.jsx";
import { formatDate } from "../i18n/index.js";
import { messageContent } from "../utils/message-content.js";
import { speakerHues } from "../utils/speaker-colors.js";

/** Load every message with its complete text; pages carry bounded previews of long turns. */
async function completeMessages(id) {
  const messages = await api.allMessages(id);
  const truncated = messages.filter(message => message.contentTruncated);
  for (let index = 0; index < truncated.length; index += 6) {
    const batch = truncated.slice(index, index + 6);
    const full = await Promise.all(batch.map(message => api.message(id, message.id).catch(() => message)));
    for (const item of full) { const target = messages.findIndex(message => message.id === item.id); if (target >= 0) messages[target] = item; }
  }
  return messages;
}

/** Split one run's turns into the preface and numbered rounds, each starting at an orchestrator question. */
export function readerRounds(messages) {
  const rounds = [];
  let current = { round: 0, items: [] };
  for (const message of messages) {
    if (message.kind === "question" && message.speaker !== "user") { if (current.items.length || current.round) rounds.push(current); current = { round: rounds.filter(item => item.round > 0).length + 1, items: [] }; }
    current.items.push(message);
  }
  if (current.items.length || current.round) rounds.push(current);
  return rounds;
}

/** Collapse a round's turns for reading: consecutive tool calls by one speaker and consecutive votes become single blocks. */
export function readerBlocks(items) {
  const blocks = [];
  for (const message of items) {
    const last = blocks.at(-1);
    if (message.kind === "tool" && last?.type === "tools" && last.speaker === message.speaker) { last.messages.push(message); continue; }
    if (message.kind === "ratification" && last?.type === "votes") { last.messages.push(message); continue; }
    if (message.kind === "tool") blocks.push({ type: "tools", key: "tools-" + message.id, speaker: message.speaker, messages: [message] });
    else if (message.kind === "ratification") blocks.push({ type: "votes", key: "votes-" + message.id, messages: [message] });
    else blocks.push({ type: "turn", key: message.id, message });
  }
  return blocks;
}

/** A vote's position and rationale from its stored reply. */
function vote(message) {
  try { const value = JSON.parse(message.content.replace(/^```(?:json)?\s*|\s*```$/g, "")); if (typeof value.position === "string") return { position: value.position, rationale: typeof value.rationale === "string" ? value.rationale : "" }; } catch { /* fall through */ }
  return { position: "", rationale: messageContent(message) };
}

function Votes({ proposal, names, hues, t }) {
  const votes = proposal.agreements.filter(vote => !vote.superseded);
  if (!votes.length) return null;
  return <table className="reader-votes"><thead><tr><th scope="col">{t("reader.agent")}</th><th scope="col">{t("reader.position")}</th><th scope="col">{t("reader.rationale")}</th></tr></thead>
    <tbody>{votes.map(vote => { const name = names.get(vote.participantId) || vote.participantId; return <tr key={vote.messageId} style={hues.has(name) ? { "--speaker-hue": hues.get(name) } : undefined}><td className="reader-vote-agent">{name}</td><td><span className={"badge vote-" + vote.position}>{t(`position.${vote.position}`, { defaultValue: vote.position })}</span></td><td>{vote.rationale}</td></tr>; })}</tbody>
  </table>;
}

/** Shape a public share bundle like the signed-in reader's data. */
export function sharedReaderData(shared) {
  const proposals = {};
  for (const proposal of shared.proposals || []) (proposals[proposal.runId] ??= []).push(proposal);
  return { conversation: { title: shared.title, openingQuestion: shared.openingQuestion }, messages: shared.messages || [], runs: shared.runs || [], proposals, shared: { expiresUtc: shared.expiresUtc, includeToolCalls: shared.includeToolCalls } };
}

/** A full-page, document-style transcript of a conversation and its debates, for reading after the fact. With a share token it is the public, read-only view. */
export default function TranscriptReader() {
  const { id, token } = useParams();
  const { t } = useTranslation();
  const [showTools, setShowTools] = useState(false);
  const [hidden, setHidden] = useState(() => new Set());
  const toggleSpeaker = name => setHidden(current => { const next = new Set(current); if (next.has(name)) next.delete(name); else next.add(name); return next; });
  const { data, error, reload } = useData("reader:" + (token ? "share" : id), async () => {
    if (token) return sharedReaderData(await api.sharedTranscript(token));
    const [conversation, messages, runs] = await Promise.all([api.conversation(id), completeMessages(id), api.runs(id)]);
    const proposals = Object.fromEntries(await Promise.all(runs.filter(run => run.protocolVersion === 2).map(async run => [run.id, await api.proposals(id, run.id).catch(() => [])])));
    return { conversation, messages, runs, proposals };
  });
  const view = useMemo(() => {
    if (!data) return null;
    const names = new Map();
    for (const run of data.runs) for (const seat of [run.orchestrator, ...(run.participants || [])]) if (seat) names.set(seat.id, seat.name);
    const hues = speakerHues(data.runs.flatMap(run => [run.orchestrator?.name, ...(run.participants || []).map(seat => seat.name)]), data.messages.map(message => message.speaker));
    const sections = [];
    const loose = data.messages.filter(message => !message.runId || !data.runs.some(run => run.id === message.runId));
    if (loose.length) sections.push({ key: "notes", run: null, messages: loose });
    for (const run of data.runs) sections.push({ key: run.id, run, messages: data.messages.filter(message => message.runId === run.id) });
    const speakers = [...new Set(data.messages.filter(message => message.kind !== "tool").map(message => message.speaker))];
    return { names, hues, sections, speakers };
  }, [data]);
  const markdown = () => "# " + data.conversation.title + "\n\n" + data.messages.filter(message => showTools || message.kind !== "tool").map(message => `## ${speakerLabel(t, message.speaker)} (${t("state." + message.kind, { defaultValue: message.kind })})\n\n${messageContent(message)}`).join("\n\n");
  const hueStyle = speaker => view.hues.has(speaker) ? { "--speaker-hue": view.hues.get(speaker) } : undefined;
  const block = item => {
    if (item.type === "tools") {
      if (!showTools || hidden.has(item.speaker)) return null;
      return <details key={item.key} className="reader-tool" style={hueStyle(item.speaker)}>
        <summary><strong>{speakerLabel(t, item.speaker)}</strong> · {t("timeline.tools", { count: item.messages.length })}: {item.messages.map(message => message.content.split("\n")[0].replace(/^Conversation tool: |^Local tool: /, "")).join(", ")}</summary>
        {item.messages.map(message => <pre key={message.id}>{message.content}</pre>)}
      </details>;
    }
    if (item.type === "votes") {
      const shown = item.messages.filter(message => !hidden.has(message.speaker));
      if (!shown.length) return null;
      return <section key={item.key} className="reader-vote-block">
        <h4>{t("reader.votes")}</h4>
        <table className="reader-votes"><thead><tr><th scope="col">{t("reader.agent")}</th><th scope="col">{t("reader.position")}</th><th scope="col">{t("reader.rationale")}</th></tr></thead>
          <tbody>{shown.map(message => { const value = vote(message); return <tr key={message.id} style={hueStyle(message.speaker)}><td className="reader-vote-agent">{speakerLabel(t, message.speaker)}</td><td>{value.position && <span className={"badge vote-" + value.position}>{t(`position.${value.position}`, { defaultValue: value.position })}</span>}</td><td>{value.rationale}</td></tr>; })}</tbody>
        </table>
      </section>;
    }
    const message = item.message;
    if (hidden.has(message.speaker)) return null;
    const kind = t("state." + (message.kind === "question" && message.speaker !== "user" ? "orchestratorQuestion" : message.kind), { defaultValue: message.kind });
    return <details key={item.key} open id={"turn-" + message.id} className={"reader-turn kind-" + message.kind + (message.speaker === "user" ? " from-moderator" : "")} style={hueStyle(message.speaker)}>
      <summary><strong>{speakerLabel(t, message.speaker)}</strong><span className="badge">{kind}</span>{["failed", "interrupted"].includes(message.state) && <span className="badge failure">{t(`state.${message.state}`, { defaultValue: message.state })}</span>}<time>{formatDate(message.createdUtc)}</time></summary>
      <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{messageContent(message)}</Markdown></div>
    </details>;
  };
  return <div className="reader-page">
    <header className="reader-bar">
      {token ? <span className="reader-shared" role="note">{t("share.banner")}{data?.shared && <> · {t("share.banner.expires", { date: formatDate(data.shared.expiresUtc) })}</>}</span> : <Link className="button" to={`/dashboard/conversations/${id}`}><Icon name="arrow" />{t("reader.back")}</Link>}
      <strong className="reader-title">{data?.conversation.title}</strong>
      {(!token || data?.shared?.includeToolCalls) && <label className="checkbox-label"><input type="checkbox" checked={showTools} onChange={event => setShowTools(event.target.checked)} />{t("reader.showTools")}</label>}
      {data && <CopyButton label="reader.copy" loadText={async () => markdown()} />}
      <Button label="reader.print" icon="download" onClick={() => window.print()} />
    </header>
    {error && <ErrorPanel error={error} retry={reload} />}
    {!data && !error && <p className="reader-loading" role="status">{t("reader.loading")}</p>}
    {view && <div className="reader-layout">
      <nav className="reader-toc" aria-label={t("reader.contents")}>
        <h2>{t("reader.contents")}</h2>
        <ol>{view.sections.map(section => <li key={section.key}><a href={"#section-" + section.key}>{section.run ? t("reader.debate", { date: formatDate(section.run.createdUtc) }) : t("reader.notes")}</a>
          {section.run && <ol>{readerRounds(section.messages).filter(round => round.round > 0).map(round => <li key={round.round}><a href={`#section-${section.key}-round-${round.round}`}>{t("timeline.round", { round: round.round })}</a></li>)}{(data.proposals[section.run.id] || []).length > 0 && <li><a href={`#section-${section.key}-outcome`}>{t("reader.outcome")}</a></li>}</ol>}
        </li>)}</ol>
      </nav>
      <main className="reader-document">
        <h1>{data.conversation.title}</h1>
        {data.conversation.openingQuestion && <p className="reader-question">{data.conversation.openingQuestion}</p>}
        <div className="reader-legend" role="group" aria-label={t("reader.speakers")}>
          <span className="muted">{t("reader.speakers")}</span>
          {view.speakers.map(name => <button key={name} type="button" className={"reader-chip" + (hidden.has(name) ? " off" : "") + (name === "user" ? " moderator" : "")} aria-pressed={!hidden.has(name)} style={hueStyle(name)} title={t(hidden.has(name) ? "reader.showSpeaker" : "reader.hideSpeaker", { name: speakerLabel(t, name) })} onClick={() => toggleSpeaker(name)}>{speakerLabel(t, name)}</button>)}
          {hidden.size > 0 && <button type="button" className="reader-chip reset" onClick={() => setHidden(new Set())}>{t("reader.showAll")}</button>}
        </div>
        {view.sections.map(section => {
          const run = section.run;
          const proposals = run ? data.proposals[run.id] || [] : [];
          const final = proposals.filter(item => !item.superseded).at(-1);
          return <article key={section.key} id={"section-" + section.key} className="reader-section">
            {run ? <header className="reader-run">
              <h2>{t("reader.debate", { date: formatDate(run.createdUtc) })}</h2>
              <p className="reader-run-meta">{t(`state.${run.state}`, { defaultValue: run.state })}{run.outcome ? " · " + t(run.protocolVersion === 2 && ["consensus", "max_rounds"].includes(run.outcome) ? `outcome.v2.${run.outcome}` : `outcome.${run.outcome}`, { defaultValue: run.outcome }) : ""} · {t("debate.round", { round: Math.min(run.round, run.maxRounds), max: run.maxRounds })}</p>
              {run.question && <blockquote>{run.question}</blockquote>}
              <p className="reader-panel">{[run.orchestrator, ...(run.participants || [])].filter(Boolean).map((seat, index) => <span key={seat.id + index} className="reader-seat" style={view.hues.has(seat.name) ? { "--speaker-hue": view.hues.get(seat.name) } : undefined}>{seat.name}{index === 0 && run.orchestrator ? " · " + t("reader.orchestrator") : ""}</span>)}</p>
            </header> : <h2>{t("reader.notes")}</h2>}
            {final && <section id={`section-${section.key}-outcome`} className="reader-outcome">
              <h3>{t("reader.finalProposal")}</h3>
              <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{final.content}</Markdown></div>
              <Votes proposal={final} names={view.names} hues={view.hues} t={t} />
            </section>}
            {(run ? readerRounds(section.messages) : [{ round: 0, items: section.messages }]).map(round => <div key={round.round} id={round.round ? `section-${section.key}-round-${round.round}` : undefined} className="reader-round">
              {round.round > 0 && <h3 className="reader-round-heading">{t("timeline.round", { round: round.round })}</h3>}
              {readerBlocks(round.items).map(block)}
            </div>)}
          </article>;
        })}
      </main>
    </div>}
  </div>;
}
