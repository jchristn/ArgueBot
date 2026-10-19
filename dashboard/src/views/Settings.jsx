import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
function SettingsForm({ snapshot, onSave }) {
  const { t } = useTranslation();
  const [value, setValue] = useState({
    revision: snapshot.revision,
    port: snapshot.settings.port,
    maxConcurrentRuns: snapshot.settings.maxConcurrentRuns,
    allowCli: snapshot.settings.allowCli,
    databaseType: snapshot.settings.database.type,
    connectionEnvironmentVariable:
      snapshot.settings.database.connectionEnvironmentVariable,
  });
  const [busy, setBusy] = useState(false);
  const change = (name, next) =>
    setValue((current) => ({ ...current, [name]: next }));
  return (
    <form
      onSubmit={async (event) => {
        event.preventDefault();
        setBusy(true);
        try {
          await onSave(value);
        } finally {
          setBusy(false);
        }
      }}
    >
      <div className="form-grid">
        <Field label="settings.port">
          {(props) => (
            <input
              {...props}
              type="number"
              min="1"
              max="65535"
              value={value.port}
              disabled={snapshot.portOverridden}
              onChange={(event) => change("port", Number(event.target.value))}
            />
          )}
        </Field>
        <Field label="settings.concurrent">
          {(props) => (
            <input
              {...props}
              type="number"
              min="1"
              max="64"
              value={value.maxConcurrentRuns}
              onChange={(event) =>
                change("maxConcurrentRuns", Number(event.target.value))
              }
            />
          )}
        </Field>
        <Field label="settings.database">
          {(props) => (
            <select
              {...props}
              value={value.databaseType}
              onChange={(event) => change("databaseType", event.target.value)}
            >
              {["Sqlite", "Postgresql"].map(
                (provider) => (
                  <option key={provider}>{provider}</option>
                ),
              )}
            </select>
          )}
        </Field>
        <Field label="settings.connectionEnv">
          {(props) => (
            <input
              {...props}
              value={value.connectionEnvironmentVariable}
              onChange={(event) =>
                change("connectionEnvironmentVariable", event.target.value)
              }
            />
          )}
        </Field>
      </div>
      <label className="checkbox-label" title={t("settings.cli.help")}>
        <input
          type="checkbox"
          title={t("settings.cli.help")}
          checked={value.allowCli}
          onChange={(event) => change("allowCli", event.target.checked)}
        />
        {t("settings.cli")}
      </label>
      <p className="muted">
        {snapshot.portOverridden
          ? t("settings.overridden")
          : t("settings.restartFields")}
      </p>
      <footer>
        <Button
          label="common.save"
          help="settings.save.help"
          className="primary"
          type="submit"
          disabled={busy}
        />
      </footer>
    </form>
  );
}
export default function Settings() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, reload } = useData("settings", () => api.settings());
  const [modal, setModal] = useState(null);
  const [restarting, setRestarting] = useState(false);
  const restart = async () => {
    setModal(null);
    setRestarting(true);
    try {
      await api.restart();
      for (let attempt = 0; attempt < 30; attempt++) {
        await new Promise((resolve) => setTimeout(resolve, 1000));
        try {
          const current = await api.settings();
          if (!current.restartRequired) {
            await reload();
            setRestarting(false);
            return;
          }
        } catch {
          /* Expected connection drop during restart. */
        }
      }
      throw new Error(t("settings.restartTimeout"));
    } catch (failure) {
      setRestarting(false);
      notify(failure);
    }
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("settings.title")}</h1>
          <p>{t("settings.description")}</p>
        </div>
        <div className="utilities">
          <Button
            label="settings.reload"
            help="common.refresh.help"
            icon="refresh"
            iconOnly
            onClick={reload}
          />
          <Button
            label="settings.restart"
            help="settings.restart.help"
            disabled={restarting}
            onClick={() => setModal("restart")}
          />
        </div>
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      <p className="product-note">{t("settings.help")}</p>
      {restarting && (
        <div className="info-banner" role="status">
          {t("settings.restarting")}
        </div>
      )}
      {data?.restartRequired && (
        <div className="info-banner" role="status">
          {t("settings.restartRequired")}
        </div>
      )}
      {data && (
        <>
          <section className="page-section settings-section">
            <header>
              <h2>{t("settings.runtime")}</h2>
              <Button
                label="common.inspect"
                help="common.inspect"
                onClick={() => setModal("json")}
              />
            </header>
            <SettingsForm
              key={data.revision}
              snapshot={data}
              onSave={async (value) => {
                try {
                  await api.saveSettings(value);
                  reload();
                } catch (failure) {
                  notify(failure);
                }
              }}
            />
          </section>
          <dl className="server-details">
            <dt>{t("settings.local")}</dt>
            <dd>
              <code>{location.origin}</code>
            </dd>
            <dt>{t("settings.effectivePort")}</dt>
            <dd>{data.effectivePort}</dd>
            <dt>{t("settings.connectionConfigured")}</dt>
            <dd>
              {t(data.connectionConfigured ? "profile.yes" : "profile.no")}
            </dd>
          </dl>
        </>
      )}
      {modal === "json" && (
        <Modal title="settings.raw" onClose={() => setModal(null)} wide>
          <pre>{JSON.stringify(data, null, 2)}</pre>
        </Modal>
      )}
      {modal === "restart" && (
        <Modal title="settings.restart" onClose={() => setModal(null)}>
          <p>{t("settings.restart.confirm")}</p>
          <footer>
            <Button
              label="common.cancel"
              help="common.cancel"
              onClick={() => setModal(null)}
            />
            <Button
              label="settings.restart"
              help="settings.restart.help"
              className="danger"
              onClick={restart}
            />
          </footer>
        </Modal>
      )}
    </>
  );
}
