import { useMemo, useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
export default function ApiExplorer() {
  const { api, notify, user } = useOutletContext();
  const { t } = useTranslation();
  const { data: spec, error, reload } = useData("openapi", () => api.openApi());
  const [search, setSearch] = useState("");
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
  const choices = operations.filter((item) =>
    (item.key + " " + item.operation.summary)
      .toLowerCase()
      .includes(search.toLowerCase()),
  );
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
        <aside className="explorer-list">
          <input
            type="search"
            value={search}
            aria-label={t("explorer.search")}
            title={t("explorer.search.help")}
            placeholder={t("explorer.search")}
            onChange={(event) => setSearch(event.target.value)}
          />
          {choices.map((item) => (
            <button
              className={
                selection?.key === item.key ? "operation selected" : "operation"
              }
              key={item.key}
              title={item.operation.summary}
              onClick={() => select(item.key)}
            >
              <b>{item.method}</b>
              <code>{item.path}</code>
            </button>
          ))}
        </aside>
        <div className="explorer-workspace">
          {selection && (
            <>
              <header>
                <code>
                  {selection.method} {selection.path}
                </code>
                <p>{selection.operation.summary}</p>
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
                      title={
                        parameter.description || t("explorer.parameter.help")
                      }
                    >
                      <span>
                        {parameter.name}
                        {parameter.required && " *"}
                      </span>
                      <input
                        value={parameters[parameter.name] || ""}
                        title={
                          parameter.description || t("explorer.parameter.help")
                        }
                        aria-label={parameter.name}
                        required={parameter.required}
                        onChange={(event) =>
                          setParameters((current) => ({
                            ...current,
                            [parameter.name]: event.target.value,
                          }))
                        }
                      />
                    </label>
                  ))}
              </div>
              {selection.operation.requestBody && (
                <Field label="explorer.body">
                  {(props) => (
                    <textarea
                      {...props}
                      rows="10"
                      spellCheck={false}
                      value={body}
                      onChange={(event) => setBody(event.target.value)}
                    />
                  )}
                </Field>
              )}
              <p className="muted">{t("explorer.auth")}</p>
              <Button
                label="explorer.execute"
                className="primary"
                disabled={
                  busy || (selection.method !== "GET" && user.role === "viewer")
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
                  <Button
                    label="common.copy"
                    help="explorer.copy.help"
                    onClick={() => navigator.clipboard.writeText(response.body)}
                  />
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
