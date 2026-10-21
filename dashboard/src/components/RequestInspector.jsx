import { useTranslation } from "react-i18next";
import CopyButton from "./CopyButton.jsx";
import CopyableId from "./CopyableId.jsx";
import { Button, Modal } from "./ui.jsx";
import { formatDate } from "../i18n/index.js";

function readable(body) {
  try { return JSON.stringify(JSON.parse(body), null, 2); } catch { return body; }
}
function CaptureBlock({ title, text, truncated = false, bytes }) {
  const { t } = useTranslation();
  return <section className="capture-block">
    <div className="capture-heading"><h4>{t(title)}</h4><CopyButton value={readable(text)} label="common.copy" disabled={!text} /></div>
    <div className="capture-meta">
      {bytes !== undefined && <span>{bytes < 0 ? t("history.unknownSize") : t("history.bytes", { count: bytes })}</span>}
      {truncated && <strong className="warning">{t("history.truncated")}</strong>}
    </div>
    {text ? <pre>{readable(text)}</pre> : <p className="muted">{t("history.noBody")}</p>}
  </section>;
}
export default function RequestInspector({ record, onClose }) {
  const { t } = useTranslation();
  const metadata = [
    ["history.recordId", <CopyableId value={record.id} />],
    ["history.time", formatDate(record.createdUtc)],
    ["history.duration", `${record.durationMs} ms`],
    ["history.requestId", <CopyableId value={record.correlationId} />],
    ["history.traceId", <CopyableId value={record.traceId} />],
  ];
  return <Modal title="history.inspect" onClose={onClose} wide className="request-inspector">
    <div className="request-overview"><span className="badge http-method">{record.method}</span><code>{record.path}</code><span className={"badge " + (record.status >= 400 ? "failure" : "success")}>HTTP {record.status}</span></div>
    <section className="request-metadata"><h3>{t("history.metadata")}</h3><dl>{metadata.map(([key, value]) => <div key={key}><dt>{t(key)}</dt><dd>{value || t("common.none")}</dd></div>)}</dl></section>
    <div className="request-response-grid">
      {[false, true].map(response => {
        const headers = record.headers.filter(header => header.response === response);
        return <section key={String(response)} className="http-capture">
          <h3>{t(response ? "history.response" : "history.request")}</h3>
          <section className="capture-block">
            <div className="capture-heading"><h4>{t("history.headers")}</h4><CopyButton value={headers.map(header => `${header.name}: ${header.value}`).join("\n")} disabled={!headers.length} /></div>
            {headers.length ? <dl className="header-values">{headers.map(header => <div key={header.name}><dt><code>{header.name}</code></dt><dd><code>{header.value}</code></dd></div>)}</dl> : <p className="muted">{t("history.noHeaders")}</p>}
          </section>
          <CaptureBlock title="history.body" text={response ? record.rawResponseBody : record.rawRequestBody} truncated={response ? record.responseTruncated : record.requestTruncated} bytes={response ? record.responseBytes : record.requestBytes} />
        </section>;
      })}
    </div>
    <details className="request-raw"><summary>{t("history.raw")}</summary><CaptureBlock title="history.raw" text={JSON.stringify(record, null, 2)} /></details>
    <footer><CopyButton label="history.copyRecord" help="history.copy.help" value={JSON.stringify(record, null, 2)} /><Button label="common.close" onClick={onClose} /></footer>
  </Modal>;
}
