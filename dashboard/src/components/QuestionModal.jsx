import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Button, ErrorPanel, Modal } from "./ui.jsx";
import { formatDate, formatList } from "../i18n/index.js";
import { randomId } from "../utils/random-id.js";

/** Empty answer state for each question of a request. */
function blank(request) {
  return Object.fromEntries((request?.questions || []).map(question => [question.id, { selected: [], other: "", otherOn: false, text: "", confirmed: null, skip: false }]));
}

/** Client-side checks that mirror the service rules, so the user sees problems before sending. */
export function validateAnswers(request, answers, t) {
  const errors = {};
  for (const question of request.questions) {
    const answer = answers[question.id];
    if (!answer || answer.skip) { if (question.required) errors[question.id] = t("questions.error.required"); continue; }
    const other = answer.otherOn ? answer.other.trim() : "";
    if (question.kind === "text") {
      const text = answer.text.trim();
      if (question.required && !text) errors[question.id] = t("questions.error.text");
      else if (text.length > question.maxLength) errors[question.id] = t("questions.error.length", { count: question.maxLength });
    } else if (question.kind === "confirm") {
      if (question.required && answer.confirmed === null) errors[question.id] = t("questions.error.confirm");
    } else {
      if (answer.otherOn && !other) { errors[question.id] = t("questions.error.other"); continue; }
      const count = answer.selected.length + (other ? 1 : 0);
      if (question.kind === "single_choice" && question.required && count !== 1) errors[question.id] = t("questions.error.single");
      if (question.kind === "multi_choice") {
        const minimum = Math.max(question.minSelections, question.required ? 1 : 0);
        if (count < minimum || answer.selected.length > question.maxSelections) errors[question.id] = t("questions.error.multi", { min: minimum, max: question.maxSelections });
      }
    }
  }
  return errors;
}

/** The request body the answer route expects. */
export function answerBody(request, answers, key) {
  return {
    idempotencyKey: key,
    answers: request.questions.map(question => {
      const answer = answers[question.id];
      if (!answer || answer.skip) return { questionId: question.id, skip: true };
      return { questionId: question.id, selected: answer.selected, otherText: answer.otherOn ? answer.other.trim() : "", text: answer.text.trim(), confirmed: answer.confirmed };
    }),
  };
}

/** Read-only view of a resolved request: who resolved it and what was chosen. */
function Resolution({ request }) {
  const { t } = useTranslation();
  return <div className="question-resolution" role="status">
    <p><strong>{t(`questions.status.${request.status}`)}</strong>{request.resolvedUtc && <> · <time dateTime={request.resolvedUtc}>{formatDate(request.resolvedUtc)}</time></>}</p>
    {request.resolutionNote && <p>{t("questions.note")}: {request.resolutionNote}</p>}
    <dl>{request.questions.map(question => <div key={question.id}><dt>{question.header}</dt><dd>{describe(question.answer, t)}</dd></div>)}</dl>
  </div>;
}

/** Text describing one stored answer. */
export function describe(answer, t) {
  if (!answer || answer.skipped) return t("questions.notAnswered");
  const parts = [...answer.selectedLabels];
  if (answer.otherText) parts.push(t("questions.otherValue", { value: answer.otherText }));
  if (answer.text) parts.push(answer.text);
  if (answer.confirmed === true) parts.push(t("common.yes"));
  if (answer.confirmed === false) parts.push(t("common.no"));
  return formatList(parts);
}

function QuestionField({ question, answer, error, onChange, index }) {
  const { t } = useTranslation();
  const id = useId();
  const set = patch => onChange({ ...answer, ...patch });
  const describedBy = [question.required ? "" : id + "-optional", error ? id + "-error" : ""].filter(Boolean).join(" ") || undefined;
  return <fieldset className={"question-field" + (error ? " invalid" : "")} aria-describedby={describedBy}>
    <legend><span className="badge">{question.header}</span> {question.prompt}</legend>
    {!question.required && <small id={id + "-optional"} className="muted">{t("questions.optional")}</small>}
    {question.kind === "text" && <>
      <textarea data-autofocus={index === 0 ? "" : undefined} aria-label={question.header} rows={4} maxLength={question.maxLength} placeholder={question.placeholder} value={answer.text} onChange={event => set({ text: event.target.value, skip: false })} />
      <small className="muted">{t("questions.characters", { count: answer.text.length, max: question.maxLength })}</small>
    </>}
    {question.kind === "confirm" && <div className="button-group" role="radiogroup" aria-label={question.header}>
      <label className="choice"><input data-autofocus={index === 0 ? "" : undefined} type="radio" name={id} checked={answer.confirmed === true} onChange={() => set({ confirmed: true, skip: false })} /> {t("common.yes")}</label>
      <label className="choice"><input type="radio" name={id} checked={answer.confirmed === false} onChange={() => set({ confirmed: false, skip: false })} /> {t("common.no")}</label>
    </div>}
    {(question.kind === "single_choice" || question.kind === "multi_choice") && <div className="choice-list" role={question.kind === "single_choice" ? "radiogroup" : "group"} aria-label={question.header}>
      {question.kind === "multi_choice" && <small className="muted">{t("questions.selectRange", { min: Math.max(question.minSelections, question.required ? 1 : 0), max: question.maxSelections })}</small>}
      {question.options.map((option, optionIndex) => {
        const checked = answer.selected.includes(option.key);
        const toggle = () => question.kind === "single_choice"
          ? set({ selected: [option.key], otherOn: false, skip: false })
          : set({ selected: checked ? answer.selected.filter(key => key !== option.key) : [...answer.selected, option.key], skip: false });
        return <label key={option.id} className="choice">
          <input data-autofocus={index === 0 && optionIndex === 0 ? "" : undefined} type={question.kind === "single_choice" ? "radio" : "checkbox"} name={id} checked={checked} onChange={toggle} />
          <span><span>{option.label}</span>{option.recommended && <> <span className="badge success">{t("questions.recommended")}</span></>}{option.description && <small className="muted">{option.description}</small>}</span>
        </label>;
      })}
      {question.allowOther && <label className="choice">
        <input type={question.kind === "single_choice" ? "radio" : "checkbox"} name={id} checked={answer.otherOn} onChange={() => set(question.kind === "single_choice" ? { otherOn: true, selected: [], skip: false } : { otherOn: !answer.otherOn, skip: false })} />
        <span>{t("questions.other")}</span>
      </label>}
      {question.allowOther && answer.otherOn && <input type="text" aria-label={t("questions.other")} maxLength={2000} value={answer.other} onChange={event => set({ other: event.target.value })} />}
    </div>}
    {!question.required && <label className="choice muted"><input type="checkbox" checked={answer.skip} onChange={event => set({ skip: event.target.checked })} /> {t("questions.leaveUnanswered")}</label>}
    {error && <p id={id + "-error"} className="field-error" role="alert">{error}</p>}
  </fieldset>;
}

/**
 * The only place a user answers an agent's question. Closing it leaves the question open and never skips it.
 * When another person resolves the question while it is open, it switches to a read-only view.
 */
export default function QuestionModal({ api, requestId, position = 1, count = 1, onPrevious, onNext, onClose, onResolved }) {
  const { t } = useTranslation();
  const [request, setRequest] = useState(null);
  const [answers, setAnswers] = useState({});
  const [errors, setErrors] = useState({});
  const [failure, setFailure] = useState(null);
  const [mode, setMode] = useState("answer");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [key, setKey] = useState(randomId);
  const [conflict, setConflict] = useState(false);
  useEffect(() => {
    let active = true;
    setRequest(null); setErrors({}); setFailure(null); setMode("answer"); setNote(""); setConflict(false); setKey(randomId());
    api.question(requestId).then(value => { if (!active) return; setRequest(value); setAnswers(blank(value)); }).catch(error => { if (active) setFailure(error); });
    return () => { active = false; };
  }, [api, requestId]);
  const refresh = async () => { const value = await api.question(requestId); setRequest(value); return value; };
  const finish = async operation => {
    setBusy(true); setFailure(null);
    try { const value = await operation(); setRequest(value); onResolved?.(value); }
    catch (error) {
      if (error.code === "question_already_resolved") { setConflict(true); await refresh().catch(() => {}); }
      else setFailure(error);
    }
    finally { setBusy(false); }
  };
  const submit = event => {
    event.preventDefault();
    const found = validateAnswers(request, answers, t);
    setErrors(found);
    if (Object.keys(found).length) return;
    finish(() => api.answerQuestion(request.id, answerBody(request, answers, key)));
  };
  const later = () => finish(async () => { const value = await api.deferQuestion(request.id); onClose(); return value; });
  const skip = () => finish(() => api.skipQuestion(request.id, { note: note.trim(), idempotencyKey: key + ":skip" }));
  const resolved = request && request.status !== "open";
  const counter = count > 1 ? t("questions.counter", { position, count }) : "";
  return <Modal title="questions.modalTitle" onClose={() => !busy && onClose()} wide className="question-modal">
    {failure && <ErrorPanel error={failure} retry={() => { setFailure(null); refresh().catch(setFailure); }} />}
    {!request && !failure && <p className="muted">{t("common.loading")}</p>}
    {request && <>
      <p className="question-meta">
        {t("questions.askedBy", { name: request.askerName || request.askerId })}
        {request.conversationTitle && <> · {t("questions.inConversation", { title: request.conversationTitle })}</>}
        {" · "}<time dateTime={request.createdUtc}>{formatDate(request.createdUtc)}</time>
        {counter && <> · <span>{counter}</span></>}
      </p>
      {request.context && <div className="markdown question-context" dir="auto"><Markdown remarkPlugins={[remarkGfm]} skipHtml>{request.context}</Markdown></div>}
      <p className={request.credentialWarning ? "warning-panel" : "muted"} role={request.credentialWarning ? "alert" : undefined}>{t(request.credentialWarning ? "questions.credentialWarning" : "questions.secretNotice")}</p>
      {conflict && <p className="warning-panel" role="alert">{t("questions.resolvedElsewhere")}</p>}
      {resolved && <Resolution request={request} />}
      {!resolved && mode === "answer" && <form onSubmit={submit} noValidate>
        {Object.keys(errors).length > 0 && <div className="error-summary" role="alert">{t("questions.error.summary", { count: Object.keys(errors).length })}</div>}
        {request.questions.map((question, index) => <QuestionField key={question.id} index={index} question={question} answer={answers[question.id]} error={errors[question.id]}
          onChange={value => { setAnswers(current => ({ ...current, [question.id]: value })); setErrors(current => { const next = { ...current }; delete next[question.id]; return next; }); setKey(randomId()); }} />)}
        <footer className="dialog-actions question-actions">
          <div className="button-group">
            <Button label="questions.later" type="button" disabled={busy || !request.wait} onClick={later} />
            <Button label="questions.skip" type="button" disabled={busy} onClick={() => setMode("skip")} />
          </div>
          <div className="button-group">
            {count > 1 && <Button label="common.previous" type="button" disabled={busy || position <= 1} onClick={onPrevious} />}
            {count > 1 && <Button label="common.next" type="button" disabled={busy || position >= count} onClick={onNext} />}
            <Button label="questions.submit" type="submit" className="primary" disabled={busy} />
          </div>
        </footer>
      </form>}
      {!resolved && mode === "skip" && <form onSubmit={event => { event.preventDefault(); skip(); }}>
        <p>{t("questions.skipHelp")}</p>
        <label className="field"><span>{t("questions.note")}</span><textarea data-autofocus="" rows={3} maxLength={2000} value={note} onChange={event => setNote(event.target.value)} /></label>
        <footer className="dialog-actions">
          <Button label="common.back" type="button" disabled={busy} onClick={() => setMode("answer")} />
          <Button label="questions.skipConfirm" type="submit" className="primary" disabled={busy} />
        </footer>
      </form>}
      {resolved && <footer className="dialog-actions">
        {count > 1 && <Button label="common.next" type="button" disabled={position >= count} onClick={onNext} />}
        <Button label="common.close" type="button" className="primary" onClick={onClose} />
      </footer>}
    </>}
  </Modal>;
}
