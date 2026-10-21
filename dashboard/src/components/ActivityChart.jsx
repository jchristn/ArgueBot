import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
export default function ActivityChart({ buckets, bucketMinutes, fromUtc, toUtc }) {
  const { t, i18n } = useTranslation();
  const container = useRef(null);
  const tooltipId = useId();
  const [width, setWidth] = useState(900);
  const [active, setActive] = useState(null);
  useEffect(() => {
    if (!globalThis.ResizeObserver) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(240, entry.contentRect.width)));
    observer.observe(container.current);
    return () => observer.disconnect();
  }, []);
  const locale = i18n.language.includes("-X") ? "en" : i18n.language;
  const number = new Intl.NumberFormat(locale);
  const axisNumber = new Intl.NumberFormat(locale, { notation: "compact", maximumFractionDigits: 1 });
  const date = new Intl.DateTimeFormat(locale, { month: "short", day: "numeric", hour: "numeric", minute: "2-digit", timeZoneName: "short" });
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const tickDate = new Intl.DateTimeFormat(locale, new Date(toUtc) - new Date(fromUtc) > 86400000 ? { month: "short", day: "numeric" } : { hour: "numeric", minute: "2-digit" });
  const maximum = Math.max(1, ...buckets.map(bucket => bucket.success + bucket.failure));
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(1, maximum / 4)));
  const step = [1, 2, 5, 10].find(value => value * magnitude >= maximum / 4) * magnitude;
  const ceiling = step * 4;
  const left = 72, right = 16, top = 16, bottom = 204;
  const plotWidth = width - left - right;
  const slot = plotWidth / Math.max(1, buckets.length);
  const tickCount = width < 500 ? 3 : 5;
  const selected = active === null ? null : buckets[active];
  const intervalEnd = bucket => new Date(Math.min(new Date(toUtc).getTime(), new Date(bucket.startUtc).getTime() + bucketMinutes * 60000));
  const description = bucket => `${date.format(new Date(bucket.startUtc))} – ${date.format(intervalEnd(bucket))}: ${t("history.success")} ${number.format(bucket.success)}, ${t("history.failure")} ${number.format(bucket.failure)}, ${t("history.total")} ${number.format(bucket.success + bucket.failure)}`;
  return (
    <figure className="activity-chart" aria-label={t("history.chart")}>
      <div className="activity-plot" ref={container} onPointerLeave={() => setActive(null)}>
        <svg viewBox={`0 0 ${width} 260`} role="group" aria-label={t("history.chart.help")} aria-describedby={selected ? tooltipId : undefined} tabIndex={0}
          onFocus={() => setActive(0)} onBlur={() => setActive(null)} onKeyDown={event => {
            if (!["ArrowLeft", "ArrowRight", "Home", "End", "Escape"].includes(event.key)) return;
            event.preventDefault();
            setActive(current => event.key === "Escape" ? null : event.key === "Home" ? 0 : event.key === "End" ? buckets.length - 1 : Math.max(0, Math.min(buckets.length - 1, (current ?? 0) + (event.key === "ArrowRight" ? 1 : -1))));
          }}>
          {Array.from({ length: 5 }, (_, index) => {
            const y = bottom - index * (bottom - top) / 4;
            return <g key={index} className="chart-axis"><path d={`M${left} ${y}H${width - right}`} /><text x={left - 8} y={y + 4} textAnchor="end">{axisNumber.format(index * step)}</text></g>;
          })}
          <text className="chart-axis-label" transform={`translate(14 ${(top + bottom) / 2}) rotate(-90)`} textAnchor="middle">{t("history.requests")}</text>
          {buckets.map((bucket, index) => {
            const success = bucket.success / ceiling * (bottom - top);
            const failure = bucket.failure / ceiling * (bottom - top);
            const x = left + index * slot;
            return <g key={bucket.startUtc} className="chart-bucket" aria-label={description(bucket)} onPointerEnter={() => setActive(index)}>
              <rect x={x + 0.5} y={bottom - success} width={Math.max(1, slot - 1)} height={success} fill="var(--success)" />
              <rect x={x + 0.5} y={bottom - success - failure} width={Math.max(1, slot - 1)} height={failure} fill="var(--danger)" />
              {index === active && <rect x={x} y={top} width={slot} height={bottom - top} className="chart-highlight" />}
              <rect x={x} y={top} width={slot} height={bottom - top} fill="transparent" />
            </g>;
          })}
          {Array.from({ length: tickCount }, (_, index) => {
            const fraction = index / (tickCount - 1);
            return <g key={index} className="chart-axis"><text x={left + fraction * plotWidth} y={225} textAnchor={index === 0 ? "start" : index === tickCount - 1 ? "end" : "middle"}>{tickDate.format(new Date(new Date(fromUtc).getTime() + fraction * (new Date(toUtc) - new Date(fromUtc))))}</text></g>;
          })}
          <text className="chart-axis-label" x={left + plotWidth / 2} y={252} textAnchor="middle">{t("history.timeAxis", { zone })}</text>
        </svg>
        {selected && <div id={tooltipId} role="tooltip" className="chart-tooltip" style={{ left: Math.max(8, Math.min(width - 248, left + (active + 0.5) * slot - 120)) }}>
          <strong>{date.format(new Date(selected.startUtc))} – {date.format(intervalEnd(selected))}</strong>
          <span><i className="chart-swatch success" />{t("history.success")}: {number.format(selected.success)}</span>
          <span><i className="chart-swatch failure" />{t("history.failure")}: {number.format(selected.failure)}</span>
          <span>{t("history.total")}: {number.format(selected.success + selected.failure)}</span>
        </div>}
      </div>
      <figcaption><span><i className="chart-swatch success" />{t("history.successLegend")}</span><span><i className="chart-swatch failure" />{t("history.failureLegend")}</span></figcaption>
    </figure>
  );
}
