import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { resourceToolLink } from "../utils/resources.js";

/** For a resource tool activity record, a link to the resource at the revision the call read or produced. */
export default function ResourceToolLink({ content }) {
  const { t } = useTranslation();
  const link = resourceToolLink(content);
  if (!link) return null;
  return <Link className="resource-link" to={"/dashboard/resources?name=" + encodeURIComponent(link.name) + "&revision=" + link.revision}>{t("resources.openRevision", { name: link.name, revision: link.revision })}</Link>;
}
