import { useTranslation } from "react-i18next";
import CopyButton from "./CopyButton.jsx";
export default function CopyableId({ value }) {
  const { t } = useTranslation();
  if (!value) return <span className="muted">{t("common.none")}</span>;
  return <span className="copyable-id" title={value}><code>{value}</code><CopyButton value={value} label="common.copyId" iconOnly /></span>;
}
