import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { formatDate } from "../i18n/index.js";
import { lineDiff } from "../utils/resources.js";
import { Button, ErrorPanel } from "./ui.jsx";

/** Saved revisions newest first, with each one's author, a diff against the current text and revert. */
export default function ResourceHistory({ api, resource, initialRevision = 0, onReverted }) {
  const { t } = useTranslation();
  const [revisions, setRevisions] = useState(null);
  const [selected, setSelected] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const open = async revision => {
    setError(null);
    try { setSelected(await api.resourceRevision(resource.id, revision)); }
    catch (failure) { setError(failure); }
  };
  useEffect(() => {
    let active = true;
    api.resourceRevisions(resource.id).then(rows => {
      if (!active) return;
      setRevisions(rows);
      const first = rows.find(row => row.revision === initialRevision) || rows.find(row => row.revision < resource.revision) || rows[0];
      if (first) api.resourceRevision(resource.id, first.revision).then(value => active && setSelected(value)).catch(failure => active && setError(failure));
    }).catch(failure => active && setError(failure));
    return () => { active = false; };
  }, [api, resource.id, resource.revision, initialRevision]);
  const revert = async () => {
    setBusy(true); setError(null);
    try { await onReverted(await api.revertResource(resource.id, selected.revision, resource.revision)); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  };
  if (!revisions) return error ? <ErrorPanel error={error} /> : <p>{t("common.loading")}</p>;
  return <div className="resource-history">
    <ol className="revision-list" aria-label={t("resources.history")}>
      {revisions.map(row => <li key={row.revision}>
        <button type="button" className={selected?.revision === row.revision ? "revision active" : "revision"} aria-current={selected?.revision === row.revision ? "true" : undefined} onClick={() => open(row.revision)}>
          <strong>{t("resources.revision", { revision: row.revision })}</strong>
          <span>{t("resources.change." + row.change)}</span>
          <span className={row.authorKind === "agent" ? "badge warning" : "badge"}>{row.authorKind === "agent" ? t("resources.agentNamed", { name: row.authorName }) : row.authorName}</span>
          <time>{formatDate(row.createdUtc)}</time>
        </button>
      </li>)}
    </ol>
    <section className="revision-detail">
      {selected && <>
        <header>
          <h3>{t("resources.diffTitle", { revision: selected.revision, current: resource.revision })}</h3>
          {selected.authorKind === "agent" && <p className="muted">{t("resources.agentRevision", { name: selected.authorName, run: selected.runId })}</p>}
          <Button label="resources.revert" type="button" disabled={busy || selected.revision === resource.revision} onClick={revert} />
        </header>
        {selected.revision === resource.revision ? <p className="muted">{t("resources.isCurrent")}</p> : <pre className="diff" aria-label={t("resources.diff")}>{lineDiff(selected.body, resource.body).map((row, index) => <div key={index} className={"diff-" + row.kind}><span aria-hidden="true">{row.kind === "added" ? "+ " : row.kind === "removed" ? "- " : "  "}</span>{row.text}</div>)}</pre>}
      </>}
      {error && <ErrorPanel error={error} />}
    </section>
  </div>;
}
