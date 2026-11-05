import { useTranslation } from "react-i18next";
import { Field } from "./ui.jsx";

const purposes = ["question", "response", "synthesis", "ratification", "compaction"];

/** One prompt choice per debate step. An empty value means the factory default. */
export default function PromptPicker({ prompts, value, onChange, disabled = false }) {
  const { t } = useTranslation();
  const custom = purposes.filter(purpose => value[purpose]).length;
  return <details className="prompt-picker" open={custom > 0}>
    <summary>{t("prompts.picker.title")} <span className="muted">· {custom ? t("prompts.picker.custom", { count: custom }) : t("prompts.picker.defaults")}</span></summary>
    <p className="muted">{t("prompts.picker.help")}</p>
    <div className="form-grid">
      {purposes.map(purpose => <Field key={purpose} label={`prompts.purpose.${purpose}`}>{props => <select {...props} disabled={disabled} value={value[purpose] || ""} onChange={event => onChange({ ...value, [purpose]: event.target.value })}>
        {prompts.filter(item => item.purpose === purpose).map(item => <option key={item.id} value={item.factory ? "" : item.id}>{item.name}</option>)}
      </select>}</Field>)}
    </div>
  </details>;
}

/** Drop empty choices so the request stores only real selections. */
export function chosenPrompts(value) {
  return Object.fromEntries(Object.entries(value || {}).filter(([, id]) => id));
}
