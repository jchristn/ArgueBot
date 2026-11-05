import CopyButton from "../components/CopyButton.jsx";
import { useMemo, useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
export default function ApiExplorer() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const { data: spec, error, reload } = useData("openapi", () => api.openApi());
  const [chosen, setChosen] = useState("");
  const [parameters, setParameters] = useState({});
  const [body, setBody] = useState("");
  const [response, setResponse] = useState(null);
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const operations = useMemo(
    () =>
      Object.entries(spec?.paths || {}).flatMap(([path, methods]) =>
        Object.entries(methods)
          .filter(([method]) =>
            ["get", "post", "put", "delete", "patch"].includes(method),
          )
          .map(([method, operation]) => ({
            key: method + " " + path,
            path,
            method: method.toUpperCase(),
            operation,
          })),
      ),
    [spec],
  );
  const selection =
    operations.find((item) => item.key === chosen) || operations[0];
  const groups = useMemo(() => operations.reduce((result, item) => { const tag = item.operation.tags?.[0] || t("explorer.other"); (result[tag] ||= []).push(item); return result; }, {}), [operations, t]);
  const resolve = (schema) => { let value = schema; for (let depth = 0; value?.$ref && depth < 8; depth++) value = spec?.components?.schemas?.[value.$ref.split("/").pop()]; return value || {}; };
  const describe = (schema) => {
    const value = resolve(schema);
    const parts = [schema?.$ref ? schema.$ref.split("/").pop() : value.type === "array" ? `${t("explorer.type.array")} ${resolve(value.items).type || value.items?.$ref?.split("/").pop() || ""}`.trim() : value.type || ""];
    if (value.enum) parts.push(t("explorer.constraint.enum", { values: value.enum.join(", ") }));
    if (value.minimum !== undefined || value.maximum !== undefined) parts.push(t("explorer.constraint.range", { minimum: value.minimum ?? "-", maximum: value.maximum ?? "-" }));
    if (value.maxLength !== undefined) parts.push(t("explorer.constraint.length", { maximum: value.maxLength }));
    if (value.default !== undefined) parts.push(t("explorer.constraint.default", { value: String(value.default) }));
    return parts.filter(Boolean).join(" · ");
  };
  const bodySchema = selection ? resolve(selection.operation.requestBody?.content?.["application/json"]?.schema) : {};
  const example = selection?.operation.requestBody?.content?.["application/json"]?.example;
  const missing = (selection?.operation.parameters || []).filter(parameter => parameter.required && !String(parameters[parameter.name] || "").trim()).map(parameter => parameter.name);
  let bodyProblem = "";
  if (selection?.operation.requestBody && selection.method !== "GET" && body.trim()) {
    try { JSON.parse(body); } catch (failure) { bodyProblem = t("explorer.invalidJson", { message: failure.message }); }
    const placeholders = [...new Set(body.match(/{{\s*[\w$]+\s*}}/g) || [])];
    if (!bodyProblem && placeholders.length) bodyProblem = t("explorer.placeholders", { names: placeholders.join(", ") });
  }
  const select = (key) => {
    setChosen(key);
    setParameters({});
    const operation = operations.find((item) => item.key === key)?.operation;
    const example =
      operation?.requestBody?.content?.["application/json"]?.example;
    setBody(example ? JSON.stringify(example, null, 2) : "");
    setResponse(null);
  };
  const execute = async () => {
    setConfirm(false);
    if (!selection) return;
    setBusy(true);
    try {
      let path = selection.path.replace(/{(\w+)}/g, (_, name) =>
        encodeURIComponent(parameters[name] || ""),
      );
      const query = new URLSearchParams();
      for (const parameter of selection.operation.parameters || [])
        if (parameter.in === "query" && parameters[parameter.name])
          query.set(parameter.name, parameters[parameter.name]);
      if (query.size) path += "?" + query;
      if (body && selection.method !== "GET") JSON.parse(body);
      setResponse(await api.executeExplorer(path, selection.method, body));
    } catch (failure) {
      notify(failure);
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("explorer.title")}</h1>
          <p>{t("explorer.description")}</p>
        </div>
        <Button
          label="common.refresh"
          icon="refresh"
          iconOnly
          onClick={reload}
        />
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      <div className="explorer">
        <Field label="explorer.operation">
          {(props) => <select {...props} className="monospace" value={selection?.key || ""} disabled={!operations.length || busy} onChange={(event) => select(event.target.value)}>
            {!operations.length && <option value="">{t("common.loading")}</option>}
            {Object.entries(groups).map(([tag, items]) => <optgroup key={tag} label={tag}>{items.map(item => <option key={item.key} value={item.key}>{item.method} {item.path} · {item.operation.summary}</option>)}</optgroup>)}
          </select>}
        </Field>
        <div className="explorer-workspace">
          {selection && (
            <>
              <header>
                <code>
                  {selection.method} {selection.path}
                </code>
                <p>{selection.operation.summary}</p>
                {selection.operation.description && <p className="muted">{selection.operation.description}</p>}
              </header>
              <div className="form-grid">
                {selection.operation.parameters
                  ?.filter(
                    (parameter) =>
                      parameter.in === "path" || parameter.in === "query",
                  )
                  .map((parameter) => (
                    <label
                      className="field"
                      key={parameter.name}
                    >
                      <span>
                        {parameter.name}
                        {parameter.required && <span className="required-mark" aria-hidden="true"> *</span>}
                      </span>
                      {parameter.schema?.type === "boolean" ? (
                        <select aria-label={parameter.name} aria-describedby={`parameter-${parameter.name}`} value={parameters[parameter.name] || ""} onChange={(event) => setParameters((current) => ({ ...current, [parameter.name]: event.target.value }))}>
                          <option value="">{t("explorer.unset")}</option>
                          <option value="true">true</option>
                          <option value="false">false</option>
                        </select>
                      ) : (
                        <input
                          type={parameter.schema?.type === "integer" ? "number" : "text"}
                          min={parameter.schema?.minimum}
                          max={parameter.schema?.maximum}
                          placeholder={parameter.schema?.default !== undefined ? String(parameter.schema.default) : parameter.in === "path" ? parameter.name : ""}
                          value={parameters[parameter.name] || ""}
                          aria-label={parameter.name}
                          aria-describedby={`parameter-${parameter.name}`}
                          aria-invalid={missing.includes(parameter.name) && Object.keys(parameters).length > 0 ? "true" : undefined}
                          required={parameter.required}
                          onChange={(event) =>
                            setParameters((current) => ({
                              ...current,
                              [parameter.name]: event.target.value,
                            }))
                          }
                        />
                      )}
                      <small id={`parameter-${parameter.name}`}>{parameter.description || t("explorer.parameter.help")}{describe(parameter.schema) && ` (${describe(parameter.schema)})`}</small>
                    </label>
                  ))}
              </div>
              {selection.operation.requestBody && (
                <>
                  <Field label="explorer.body">
                    {(props) => (
                      <textarea
                        {...props}
                        rows="10"
                        spellCheck={false}
                        className="monospace"
                        aria-invalid={bodyProblem ? "true" : undefined}
                        value={body}
                        onChange={(event) => setBody(event.target.value)}
                      />
                    )}
                  </Field>
                  {bodyProblem && <p className="field-error" role="alert">{bodyProblem}</p>}
                  <div className="button-group">
                    {example && <Button label="explorer.resetExample" onClick={() => setBody(JSON.stringify(example, null, 2))} />}
                  </div>
                  {bodySchema.properties && <details className="explorer-fields"><summary>{t("explorer.bodyFields", { name: selection.operation.requestBody.content["application/json"].schema?.$ref?.split("/").pop() || "" })}</summary>
                    <table>
                      <thead><tr><th scope="col">{t("explorer.field")}</th><th scope="col">{t("explorer.fieldType")}</th><th scope="col">{t("explorer.fieldDescription")}</th></tr></thead>
                      <tbody>{Object.entries(bodySchema.properties).map(([name, property]) => <tr key={name}><th scope="row"><code>{name}</code>{bodySchema.required?.includes(name) && <span className="required-mark" title={t("explorer.requiredField")}> *</span>}</th><td>{describe(property)}</td><td>{property.description || resolve(property).description || ""}</td></tr>)}</tbody>
                    </table>
                  </details>}
                </>
              )}
              {missing.length > 0 && <p className="muted" role="status">{t("explorer.missing", { names: missing.join(", ") })}</p>}
              <p className="muted">{t("explorer.auth")}</p>
              <Button
                label="explorer.execute"
                className="primary"
                disabled={
                  busy || missing.length > 0 || Boolean(bodyProblem)
                }
                onClick={() =>
                  selection.method === "GET" ? execute() : setConfirm(true)
                }
              />
              {response && (
                <section className="explorer-response">
                  <h2>
                    {t("explorer.response")}{" "}
                    <span className="badge">{response.status}</span>
                  </h2>
                  <p>{response.durationMs} ms</p>
                  <table>
                    <tbody>
                      {response.headers.map(([name, value]) => (
                        <tr key={name}>
                          <th scope="row">{name}</th>
                          <td>
                            <code>{value}</code>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <pre>{response.body}</pre>
                  <CopyButton value={response.body} help="explorer.copy.help" />
                </section>
              )}
            </>
          )}
        </div>
      </div>
      {confirm && (
        <Modal title="explorer.confirm" onClose={() => setConfirm(false)}>
          <p>{t("explorer.confirm.help")}</p>
          <code>{selection?.key}</code>
          <footer>
            <Button
              label="common.cancel"
              help="common.cancel"
              onClick={() => setConfirm(false)}
            />
            <Button
              label="explorer.execute"
              className="danger"
              onClick={execute}
            />
          </footer>
        </Modal>
      )}
    </>
  );
}
