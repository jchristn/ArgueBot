import i18n from "i18next";
import ICU from "i18next-icu";
import { initReactI18next } from "react-i18next";
import en from "../../../assets/i18n/en.json";
import registry from "../../../assets/i18n/registry.json";
export { registry };
const expand = (value) =>
  `［${value} ${"·".repeat(Math.ceil(value.length * 0.35))}］`;
const expanded = Object.fromEntries(
  Object.entries(en).map(([key, value]) => [key, expand(value)]),
);
const locale = localStorage.getItem("arguebot.locale") || "en";
await i18n
  .use(ICU)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      "en-XA": { translation: expanded },
      "ar-XB": { translation: en },
    },
    lng: registry.some((item) => item.code === locale) ? locale : "en",
    fallbackLng: "en",
    keySeparator: false,
    interpolation: { escapeValue: false },
  });
function apply() {
  const record =
    registry.find((item) => item.code === i18n.language) || registry[0];
  document.documentElement.lang = record.code;
  document.documentElement.dir = record.dir;
  localStorage.setItem("arguebot.locale", record.code);
}
i18n.on("languageChanged", apply);
apply();
export const formatDate = (value) =>
  new Intl.DateTimeFormat(i18n.language.includes("-X") ? "en" : i18n.language, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
export default i18n;
