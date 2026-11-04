import MemoryPanel from "./MemoryPanel.jsx";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useData } from "./useData.js";
import { Button, ErrorPanel, Field, Modal } from "./ui.jsx";
import { randomId } from "../utils/random-id.js";

export default function DebatePanel({ api, id, run, profiles, notify, onChange }) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [action, setAction] = useState("add");
  const [seat, setSeat] = useState("");
  const [profile, setProfile] = useState("");
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState(randomId);
  const [revising, setRevising] = useState(null);
  const [revisionText, setRevisionText] = useState("");
  const [revisionKey, setRevisionKey] = useState(randomId);
  const [revisionError, setRevisionError] = useState("");
  const { data, error, reload } = useData(`${run.id}:${run.revision}`, async () => {
    const [proposals, roster, receipts] = await Promise.all([api.proposals(id, run.id), api.roster(id, run.id), api.interjections(id)]);
    return { proposals, roster, receipts: receipts.filter(item => item.runId === run.id) };
  });
  const active = !["completed", "cancelled", "failed"].includes(run.state);
  const save = async event => {
    event.preventDefault(); setBusy(true);
    try {
      await api.changeRoster(id, run.id, { action, participantId: seat, profileId: profile, revision: run.revision, idempotencyKey: key });
      setEditing(false); setKey(randomId()); await reload(); await onChange();
    } catch (failure) { notify(failure); }
    finally { setBusy(false); }
  };
  const canRevise = proposal => active && run.state !== "stopping" && run.phase === "ratification" && proposal.id === run.proposalId;
  const startRevision = proposal => { setRevising(proposal); setRevisionText(proposal.content); setRevisionKey(randomId()); setRevisionError(""); };
  const revise = async event => {
    event.preventDefault(); setBusy(true); setRevisionError("");
    try {
      await api.action(id, run.id, { action: "edit_proposal", text: revisionText, revision: run.revision, idempotencyKey: revisionKey });
      setRevising(null); await reload(); await onChange();
    } catch (failure) {
      if (failure.field === "text") setRevisionError(failure.message);
      else notify(failure);
    } finally { setBusy(false); }
  };
  return <section className="debate-panel" id="debate-panel" aria-label={t("panel.title")}>
    <header className="panel-heading">
      <div><strong>{t("panel.title")}</strong><p className="muted">{t("panel.phase", { phase: t(`phase.${run.phase}`, { defaultValue: run.phase }), count: run.participants.length, epoch: run.contextEpoch })}</p></div>
      <Button label="panel.change" disabled={!active} onClick={() => setEditing(true)} />
    </header>
    <p>{t("panel.unanimous")}</p>
    {run.policy && run.usage && <details><summary>{t("policy.usage")}</summary><dl className="policy-usage">{[["participantTurns", "maxParticipantTurns"], ["orchestrationCalls", "maxOrchestrationCalls"], ["compactionCalls", "maxCompactionCalls"], ["toolCalls", "maxToolCalls"], ["estimatedInputTokens", "maxEstimatedInputTokens"]].map(([used, limit]) => <div key={used}><dt>{t(`policy.${limit}`)}</dt><dd>{run.usage[used]} / {run.policy[limit] || t("policy.noLimit")}</dd></div>)}</dl><p>{t("policy.deadline", { time: run.policy.maxWallClockSeconds ? new Date(new Date(run.createdUtc).getTime() + run.policy.maxWallClockSeconds * 1000).toLocaleString() : t("policy.noLimit") })}</p><p className="muted">{t("policy.estimateHelp")}</p></details>}
    {run.prompts?.length > 0 && <details className="run-prompts"><summary>{t("prompts.used")}</summary><dl className="server-details">{run.prompts.map(item => <div key={item.purpose}><dt>{t(`prompts.purpose.${item.purpose}`)}</dt><dd>{item.name} <span className="muted">· {t("prompts.revision", { revision: item.promptRevision })}</span></dd></div>)}</dl></details>}
    {run.currentQuestion && <blockquote>{run.currentQuestion}</blockquote>}
    {error && <ErrorPanel error={error} retry={reload} />}
    {data && <>
      <div className="panel-seats">{data.roster.map(member => <span key={member.participantId} className="badge">{member.name}{!member.active && ` · ${t("panel.removed")}`}</span>)}</div>
      <details className="proposals" open={!active}><summary>{t("panel.proposals", { count: data.proposals.length })}</summary>
        {!data.proposals.length && <p className="muted">{t("panel.noProposal")}</p>}
        {data.proposals.map(proposal => <article className={proposal.superseded ? "proposal superseded" : "proposal"} key={proposal.id}>
          <div className="proposal-heading">
            <p className="muted">{t("panel.proposalContext", { epoch: proposal.contextEpoch, roster: proposal.rosterRevision })}</p>
            {proposal.moderatorEdited && <span className="badge" title={t("panel.moderatorRevision.help")}>{t("panel.moderatorRevision")}</span>}
            {proposal.superseded && <span className="badge" title={t("panel.replacedProposal.help")}>{t("panel.replacedProposal")}</span>}
            {canRevise(proposal) && <Button label="panel.editProposal" disabled={busy} onClick={() => startRevision(proposal)} />}
          </div>
          <div className="markdown"><Markdown remarkPlugins={[remarkGfm]}>{proposal.content}</Markdown></div>
          <p className="muted">{proposal.superseded ? t("panel.replacedTally", { count: proposal.agreements.length }) : proposal.agreements.some(vote => !vote.superseded) ? t("panel.tally", { agree: proposal.agreements.filter(vote => !vote.superseded && vote.position === "agree").length, count: proposal.agreements.filter(vote => !vote.superseded).length }) : t("panel.noVotes")}</p>
          <ul className="agreement-list">{[...proposal.agreements].sort((a, b) => Number(a.superseded) - Number(b.superseded)).map(vote => <li key={vote.messageId + (vote.superseded ? ":old" : "")} className={vote.superseded ? "superseded" : ""}><strong>{data.roster.find(member => member.participantId === vote.participantId)?.name || vote.participantId}</strong><span className="badge">{t(`position.${vote.position}`, { defaultValue: vote.position })}</span>{vote.superseded && <span className="badge" title={t("position.superseded.help")}>{t("position.superseded")}</span>}<p>{vote.rationale}</p></li>)}</ul>
        </article>)}
      </details>
      {data.receipts.length > 0 && <details><summary>{t("panel.contextHistory")}</summary><ul>{data.receipts.map(receipt => <li key={receipt.id}>{t(`delivery.${receipt.deliveryMode}`)} · {t(`delivery.${receipt.status}`, { defaultValue: receipt.status })} · {t("panel.seen", { count: receipt.deliveredParticipantIds.length })} · {t("panel.summarized", { count: receipt.summarizedParticipantIds?.length || 0 })}</li>)}</ul></details>}
    </>}
    <MemoryPanel api={api} id={id} run={run} notify={notify} onChange={onChange} />
    {revising && <Modal title="panel.editProposal" wide onClose={() => !busy && setRevising(null)}><form onSubmit={revise}>
      <p>{t("panel.editProposalHelp")}</p>
      <Field label="panel.revisedProposal">{props => <textarea {...props} rows="10" required maxLength={32000} aria-invalid={revisionError ? "true" : undefined} value={revisionText} onChange={event => { setRevisionText(event.target.value); setRevisionKey(randomId()); setRevisionError(""); }} />}</Field>
      {revisionError && <p className="field-error" role="alert">{revisionError}</p>}
      <footer className="dialog-actions"><Button label="common.cancel" type="button" disabled={busy} onClick={() => setRevising(null)} /><Button label="panel.saveRevision" type="submit" className="primary" disabled={busy || !revisionText.trim() || revisionText.trim() === revising.content.trim()} /></footer>
    </form></Modal>}
    {editing && <Modal title="panel.change" onClose={() => !busy && setEditing(false)}><form onSubmit={save}>
      <p>{t("panel.changeHelp")}</p>
      <Field label="panel.action">{props => <select {...props} value={action} onChange={event => { setAction(event.target.value); setKey(randomId()); }}><option value="add">{t("panel.add")}</option><option value="remove">{t("panel.remove")}</option><option value="replace">{t("panel.replace")}</option></select>}</Field>
      {action !== "add" && <Field label="panel.seat">{props => <select {...props} required value={seat} onChange={event => { setSeat(event.target.value); setKey(randomId()); }}><option value="">{t("panel.choose")}</option>{run.participants.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>}</Field>}
      {action !== "remove" && <Field label="panel.profile">{props => <select {...props} required value={profile} onChange={event => { setProfile(event.target.value); setKey(randomId()); }}><option value="">{t("panel.choose")}</option>{profiles.filter(item => item.enabled).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>}</Field>}
      <footer className="dialog-actions"><Button label="common.cancel" type="button" disabled={busy} onClick={() => setEditing(false)} /><Button label="panel.submit" type="submit" className="primary" disabled={busy || (action !== "add" && !seat) || (action !== "remove" && !profile)} /></footer>
    </form></Modal>}
  </section>;
}
