import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";

/**
 * URL-synced page tabs. The active tab lives in the query string (default `?tab=`) so links and
 * reloads land on the same tab; only the active panel is mounted. Keyboard support follows the
 * WAI-ARIA tabs pattern (arrow keys, Home and End).
 */
export default function Tabs({ tabs, param = "tab", label }) {
  const { t } = useTranslation();
  const [search, setSearch] = useSearchParams();
  const requested = search.get(param);
  const active = tabs.find(tab => tab.key === requested) || tabs[0];
  const select = key => {
    const next = new URLSearchParams(search);
    next.set(param, key);
    setSearch(next, { replace: true });
    requestAnimationFrame(() => document.getElementById(`tab-${param}-${key}`)?.focus());
  };
  const onKeyDown = event => {
    const index = tabs.indexOf(active);
    const moves = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: tabs.length - 1 };
    if (!(event.key in moves)) return;
    event.preventDefault();
    const target = (moves[event.key] + tabs.length) % tabs.length;
    select(tabs[target].key);
  };
  return <div className="page-tabs">
    <div className="page-tabs-list" role="tablist" aria-label={label ? t(label) : undefined} onKeyDown={onKeyDown}>
      {tabs.map(tab => {
        const selected = tab === active;
        return <button key={tab.key} type="button" role="tab" id={`tab-${param}-${tab.key}`} aria-selected={selected} aria-controls={`tabpanel-${param}-${tab.key}`} tabIndex={selected ? 0 : -1} className={selected ? "page-tab active" : "page-tab"} title={tab.help ? t(tab.help) : undefined} onClick={() => select(tab.key)}>
          {t(tab.label)}{typeof tab.count === "number" && <span className="page-tab-count">{tab.count}</span>}
        </button>;
      })}
    </div>
    <div role="tabpanel" id={`tabpanel-${param}-${active.key}`} aria-labelledby={`tab-${param}-${active.key}`} className="page-tab-panel">{active.render()}</div>
  </div>;
}
