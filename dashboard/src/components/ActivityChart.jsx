import { useTranslation } from "react-i18next";
export default function ActivityChart({ buckets }) {
  const { t, i18n } = useTranslation();
  const maximum = Math.max(
    1,
    ...buckets.map((bucket) => bucket.success + bucket.failure),
  );
  const width = 900;
  const height = 110;
  const gap = 3;
  const bar = Math.max(1, width / Math.max(1, buckets.length) - gap);
  return (
    <figure
      className="activity-chart"
      aria-label={t("history.chart.help")}
      title={t("history.chart.help")}
    >
      <svg
        viewBox={`0 0 ${width} ${height + 20}`}
        role="img"
        aria-label={t("history.chart")}
        preserveAspectRatio="none"
      >
        {buckets.map((bucket, index) => {
          const success = (bucket.success / maximum) * height;
          const failure = (bucket.failure / maximum) * height;
          return (
            <g key={bucket.startUtc}>
              <title>
                {new Intl.DateTimeFormat(
                  i18n.language.includes("-X") ? "en" : i18n.language,
                  { hour: "numeric", minute: "numeric" },
                ).format(new Date(bucket.startUtc))}
                : {bucket.success + bucket.failure}
              </title>
              <rect
                x={index * (bar + gap)}
                y={height - success}
                width={bar}
                height={success}
                fill="var(--accent)"
              />
              <rect
                x={index * (bar + gap)}
                y={height - success - failure}
                width={bar}
                height={failure}
                fill="var(--danger)"
              />
            </g>
          );
        })}
        <path d={`M0 ${height + 1}H${width}`} stroke="var(--border)" />
      </svg>
      <figcaption>
        <span>{t("history.success")}</span>
        <span>{t("history.failure")}</span>
      </figcaption>
    </figure>
  );
}
