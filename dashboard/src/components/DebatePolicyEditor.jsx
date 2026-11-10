import { useState } from "react";
import { useTranslation } from "react-i18next";
import { parseTags } from "../utils/resources.js";
import { Field } from "./ui.jsx";

export const resourceAccessModes = ["off", "read", "read_write"];

/** A comma-separated list field that keeps the typed text and reports the parsed list. */
function ListField({ label, value, onChange }) {
  const [text, setText] = useState(() => (value || []).join(", "));
  return <Field label={label}>{props => <input {...props} value={text} onChange={event => { setText(event.target.value); onChange(parseTags(event.target.value)); }} />}</Field>;
}

export default function DebatePolicyEditor({ value, maximum, onChange }) {
  const { t } = useTranslation();
  const change = (key, next) => onChange({ ...value, [key]: next });
  return <details className="debate-policy"><summary>{t("policy.title")}</summary><p className="muted">{t("policy.help")}</p>
    <div className="form-grid">{["maxParticipantTurns", "maxOrchestrationCalls", "maxCompactionCalls", "maxToolCalls", "maxWallClockSeconds", "maxContextCharacters", "maxEstimatedInputTokens", "maxStepRetries"].map(key => <Field key={key} label={`policy.${key}`}>{props => <input {...props} type="number" required min={key === "maxEstimatedInputTokens" || key === "maxStepRetries" ? 0 : key === "maxContextCharacters" ? 8192 : 1} max={maximum[key]} value={value[key]} onChange={event => change(key, Number(event.target.value))} />}</Field>)}</div>
    <p className="muted">{t("policy.estimateHelp")}</p>
    <fieldset className="resource-policy">
      <legend>{t("policy.resources")}</legend>
      <p className="muted">{t("policy.resources.help")}</p>
      <div className="form-grid">
        <Field label="policy.resourceAccess">{props => <select {...props} value={value.resourceAccess || "read"} onChange={event => change("resourceAccess", event.target.value)}>{resourceAccessModes.map(mode => <option key={mode} value={mode}>{t("policy.resourceAccess." + mode)}</option>)}</select>}</Field>
        <ListField label="policy.resourceNames" value={value.resourceNames} onChange={next => change("resourceNames", next)} />
        <ListField label="policy.resourceTags" value={value.resourceTags} onChange={next => change("resourceTags", next)} />
      </div>
      {value.resourceAccess === "read_write" && <div className="form-grid">{["maxResourceWritesPerTurn", "maxResourceWriteBytesPerTurn", "maxResourcesCreated"].map(key => <Field key={key} label={`policy.${key}`}>{props => <input {...props} type="number" required min={0} max={maximum[key]} value={value[key]} onChange={event => change(key, Number(event.target.value))} />}</Field>)}</div>}
    </fieldset>
    <fieldset className="question-policy">
      <legend>{t("policy.questions")}</legend>
      <p className="muted">{t("policy.questions.help")}</p>
      <div className="form-grid">
        <Field label="policy.userQuestions">{props => <select {...props} value={value.userQuestions || "all"} onChange={event => change("userQuestions", event.target.value)}>{["off", "orchestrator", "all"].map(mode => <option key={mode} value={mode}>{t("policy.userQuestions." + mode)}</option>)}</select>}</Field>
        <Field label="policy.intake">{props => <select {...props} value={value.intake || "off"} onChange={event => change("intake", event.target.value)}>{["off", "ask_if_needed"].map(mode => <option key={mode} value={mode}>{t("policy.intake." + mode)}</option>)}</select>}</Field>
        <Field label="policy.resumeOnAnswer">{props => <select {...props} value={value.resumeOnAnswer === false ? "pause" : "resume"} onChange={event => change("resumeOnAnswer", event.target.value === "resume")}><option value="resume">{t("policy.resumeOnAnswer.resume")}</option><option value="pause">{t("policy.resumeOnAnswer.pause")}</option></select>}</Field>
      </div>
      {value.userQuestions !== "off" && <div className="form-grid">{[["questionHoldSeconds", 0], ["questionExpirySeconds", 0], ["maxQuestionRequestsPerTurn", 0], ["maxQuestionRequestsPerRun", 0], ["maxIntakeQuestions", 1]].map(([key, minimum]) => <Field key={key} label={`policy.${key}`}>{props => <input {...props} type="number" required min={minimum} max={maximum[key]} value={value[key]} onChange={event => change(key, Number(event.target.value))} />}</Field>)}</div>}
    </fieldset>
    {["questionInstructions", "synthesisInstructions", "summaryInstructions"].map(key => <Field key={key} label={`policy.${key}`}>{props => <textarea {...props} rows={3} maxLength={4000} value={value[key]} onChange={event => change(key, event.target.value)} />}</Field>)}
  </details>;
}
