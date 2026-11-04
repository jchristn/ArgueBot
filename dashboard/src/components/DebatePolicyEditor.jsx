import { useTranslation } from "react-i18next";
import { Field } from "./ui.jsx";

export default function DebatePolicyEditor({ value, maximum, onChange }) {
  const { t } = useTranslation();
  const change = (key, next) => onChange({ ...value, [key]: next });
  return <details className="debate-policy"><summary>{t("policy.title")}</summary><p className="muted">{t("policy.help")}</p>
    <div className="form-grid">{["maxParticipantTurns", "maxOrchestrationCalls", "maxCompactionCalls", "maxToolCalls", "maxWallClockSeconds", "maxContextCharacters", "maxEstimatedInputTokens"].map(key => <Field key={key} label={`policy.${key}`}>{props => <input {...props} type="number" required min={key === "maxEstimatedInputTokens" ? 0 : key === "maxContextCharacters" ? 8192 : 1} max={maximum[key]} value={value[key]} onChange={event => change(key, Number(event.target.value))} />}</Field>)}</div>
    <p className="muted">{t("policy.estimateHelp")}</p>
    {["questionInstructions", "synthesisInstructions", "summaryInstructions"].map(key => <Field key={key} label={`policy.${key}`}>{props => <textarea {...props} rows={3} maxLength={4000} value={value[key]} onChange={event => change(key, event.target.value)} />}</Field>)}
  </details>;
}
