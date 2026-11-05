import { useTranslation } from "react-i18next";
import i18n, { registry } from "../i18n/index.js";
import { Icon } from "./ui.jsx";

/** Interface language selector. */
export function Language() {
  const { t } = useTranslation();
  return (
    <select
      className="language"
      aria-label={t("nav.language")}
      title={t("nav.language")}
      value={i18n.language}
      onChange={(event) => i18n.changeLanguage(event.target.value)}
    >
      {registry.map((locale) => (
        <option key={locale.code} value={locale.code}>
          {locale.nativeName}
        </option>
      ))}
    </select>
  );
}

/** Light and dark theme switch. */
export function ThemeControl({ theme, toggle }) {
  const { t } = useTranslation();
  const label = t("nav.theme", { theme: theme === "dark" ? "light" : "dark" });
  return (
    <button className="button icon-button" onClick={toggle} title={label} aria-label={label}>
      <Icon name={theme === "dark" ? "sun" : "moon"} />
    </button>
  );
}
