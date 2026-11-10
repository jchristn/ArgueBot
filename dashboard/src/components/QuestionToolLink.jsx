import { useTranslation } from "react-i18next";
import { questionToolRequest } from "../utils/questions.js";
import { useQuestions } from "./QuestionHost.jsx";
import { Button } from "./ui.jsx";

/** For an ask_user tool activity record, a button that opens the question in the question modal. Answers are never given inline. */
export default function QuestionToolLink({ content }) {
  const { t } = useTranslation();
  const { available, open } = useQuestions();
  const requestId = questionToolRequest(content);
  if (!requestId || !available) return null;
  return <Button label="questions.open" className="link-button" title={t("questions.open.help")} onClick={() => open(requestId)} />;
}
