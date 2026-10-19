import { useEffect, useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import {
  Button,
  ErrorPanel,
  Field,
  Modal,
  Pagination,
} from "../components/ui.jsx";

export default function Backups() {
  const { api, user, notify } = useOutletContext();
  const { t, i18n } = useTranslation();
  const [filter, setFilter] = useState({
    search: "",
    pageNumber: 1,
    pageSize: 25,
  });
  const { data, error, loading, reload } = useData(JSON.stringify(filter), () =>
    api.backups(filter),
  );
  const [jobFilter, setJobFilter] = useState({ pageNumber: 1, pageSize: 25 });
  const {
    data: jobs,
    error: jobsError,
    reload: reloadJobs,
  } = useData(JSON.stringify(jobFilter), () =>
    api.recoveryOperations(jobFilter),
  );
  const [dialog, setDialog] = useState(null);
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [includeSecrets, setIncludeSecrets] = useState(false);
  const [restoreDatabaseType, setRestoreDatabaseType] = useState("Sqlite");
  const [
    restoreConnectionEnvironmentVariable,
    setRestoreConnectionEnvironmentVariable,
  ] = useState("");
  const [port, setPort] = useState(4537);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState(null);
  const [operation, setOperation] = useState(null);
  useEffect(() => {
    if (!operation || !["queued", "running"].includes(operation.state)) return;
    let disposed = false;
    const timer = setTimeout(async () => {
      try {
        const result = await api.recoveryOperation(operation.id);
        if (!disposed) {
          setOperation(result);
          if (!["queued", "running"].includes(result.state)) {
            reload();
            reloadJobs();
          }
        }
      } catch (error) {
        if (!disposed) setFailure(error);
      }
    }, 500);
    return () => {
      disposed = true;
      clearTimeout(timer);
    };
  }, [operation, api, reload, reloadJobs]);
  const close = () => {
    setDialog(null);
    setPassword("");
    setFailure(null);
  };
  const open = (kind, artifact) => {
    setName("");
    setPassword("");
    setIncludeSecrets(false);
    setRestoreDatabaseType("Sqlite");
    setRestoreConnectionEnvironmentVariable("");
    setFailure(null);
    setDialog({ kind, artifact });
  };
  if (!user.isAdmin) return <p role="alert">{t("backups.admin")}</p>;
  return (
    <>
      <header className="page-heading">
        <div>
          <h1>{t("backups.title")}</h1>
          <p>{t("backups.description")}</p>
        </div>
        <Button
          label="backups.create"
          icon="plus"
          onClick={() => open("create")}
        />
      </header>
      {error && <ErrorPanel error={error} retry={reload} />}
      {failure && !dialog && <ErrorPanel error={failure} />}
      {operation && (
        <section className="card" aria-live="polite">
          <h2>{t("backups.operation")}</h2>
          <p title={t("backups.operation.help")}>
            {operation.kind}: {operation.state} <code>{operation.id}</code>
          </p>
          {operation.errorCode && <p role="alert">{operation.errorCode}</p>}
          {operation.targetHome && <p>{operation.targetHome}</p>}
          {operation.retainedStagingHome && (
            <p role="alert" title={t("backups.retained.help")}>
              {t("backups.retained")}:{" "}
              <code>{operation.retainedStagingHome}</code>
            </p>
          )}
          {operation.missingReferences?.length > 0 && (
            <p>
              {t("backups.missing")}: {operation.missingReferences.join(", ")}
            </p>
          )}
        </section>
      )}
      <Field label="backups.search">
        {(props) => (
          <input
            {...props}
            value={filter.search}
            onChange={(event) =>
              setFilter({
                ...filter,
                search: event.target.value,
                pageNumber: 1,
              })
            }
          />
        )}
      </Field>
      {loading ? (
        <p>{t("common.loading")}</p>
      ) : data?.items.length === 0 ? (
        <p>{t("backups.empty")}</p>
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                {["name", "created", "size", "status", "actions"].map((key) => (
                  <th key={key} title={t(`backups.${key}.help`)}>
                    {t(`backups.${key}`)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data?.items.map((artifact) => (
                <tr key={artifact.id}>
                  <td title={artifact.id}>
                    {artifact.name}
                    {artifact.encrypted && (
                      <small> · {t("backups.encrypted")}</small>
                    )}
                    {artifact.includesSecrets && (
                      <small> · {t("backups.secrets")}</small>
                    )}
                  </td>
                  <td title={artifact.createdUtc}>
                    {new Intl.DateTimeFormat(i18n.language, {
                      dateStyle: "medium",
                      timeStyle: "short",
                    }).format(new Date(artifact.createdUtc))}
                  </td>
                  <td title={t("backups.size.help")}>
                    {new Intl.NumberFormat(i18n.language, {
                      style: "unit",
                      unit: "byte",
                    }).format(artifact.bytes)}
                  </td>
                  <td title={t("backups.status.help")}>{artifact.status}</td>
                  <td>
                    <div className="actions">
                      <Button
                        label="backups.download"
                        icon="download"
                        iconOnly
                        disabled={artifact.status !== "ready"}
                        onClick={async () => {
                          try {
                            await api.downloadBackup(artifact.id);
                          } catch (error) {
                            notify(error);
                          }
                        }}
                      />
                      <Button
                        label="backups.verify"
                        disabled={artifact.status !== "ready"}
                        onClick={() => open("verify", artifact)}
                      />
                      <Button
                        label="backups.restore"
                        disabled={artifact.status !== "ready"}
                        onClick={() => open("restore", artifact)}
                      />
                      <Button
                        label="backups.delete"
                        onClick={() => open("delete", artifact)}
                      />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {data && (
        <Pagination
          page={data}
          onChange={(next) => setFilter({ ...filter, ...next })}
        />
      )}
      <section className="page-section">
        <header className="page-heading">
          <h2>{t("backups.jobs")}</h2>
          <Button label="common.refresh" onClick={reloadJobs} />
        </header>
        {jobsError && <ErrorPanel error={jobsError} retry={reloadJobs} />}
        {jobs?.items.length === 0 && <p>{t("backups.noJobs")}</p>}
        {jobs?.items.length > 0 && (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  {["operation", "status", "artifact", "actions"].map((key) => (
                    <th key={key} title={t(`backups.${key}.help`)}>
                      {t(`backups.${key}`)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {jobs.items.map((job) => (
                  <tr key={job.id}>
                    <td title={job.id}>{job.kind}</td>
                    <td title={t("backups.operation.help")}>{job.state}</td>
                    <td>
                      <code title={t("backups.artifact.help")}>
                        {job.backupId}
                      </code>
                    </td>
                    <td>
                      <Button
                        label="backups.viewOperation"
                        onClick={() => setOperation(job)}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {jobs && (
          <Pagination
            page={jobs}
            onChange={(next) => setJobFilter({ ...jobFilter, ...next })}
          />
        )}
      </section>
      {dialog && (
        <Modal title={`backups.${dialog.kind}`} onClose={close}>
          <form
            onSubmit={async (event) => {
              event.preventDefault();
              setBusy(true);
              setFailure(null);
              try {
                if (dialog.kind === "delete") {
                  await api.deleteBackup(dialog.artifact.id);
                  reload();
                  reloadJobs();
                  setOperation((current) =>
                    current?.backupId === dialog.artifact.id ? null : current,
                  );
                } else
                  setOperation(
                    await api.recovery(dialog.kind, dialog.artifact?.id, {
                      name: name || t("backups.defaultName"),
                      password,
                      includeSecrets,
                      destinationName: name,
                      restoreDatabaseType,
                      restoreConnectionEnvironmentVariable:
                        restoreDatabaseType === "Postgresql"
                          ? restoreConnectionEnvironmentVariable
                          : "",
                      port: dialog.kind === "restore" ? port : 0,
                    }),
                  );
                close();
              } catch (error) {
                setFailure(error);
              } finally {
                setBusy(false);
              }
            }}
          >
            <p>{t(`backups.${dialog.kind}.help`)}</p>
            {dialog.kind === "delete" ? (
              <p>{dialog.artifact.name}</p>
            ) : (
              <>
                {dialog.kind === "create" && (
                  <Field label="backups.name">
                    {(props) => (
                      <input
                        {...props}
                        value={name}
                        maxLength={200}
                        onChange={(event) => setName(event.target.value)}
                      />
                    )}
                  </Field>
                )}
                {dialog.kind === "restore" && (
                  <>
                    <Field label="backups.destination">
                      {(props) => (
                        <input
                          {...props}
                          value={name}
                          pattern="[A-Za-z0-9][A-Za-z0-9_-]{0,63}"
                          required
                          onChange={(event) => setName(event.target.value)}
                        />
                      )}
                    </Field>
                    <Field label="backups.provider">
                      {(props) => (
                        <select
                          {...props}
                          value={restoreDatabaseType}
                          onChange={(event) =>
                            setRestoreDatabaseType(event.target.value)
                          }
                        >
                          <option value="Sqlite">SQLite</option>
                          <option value="Postgresql">PostgreSQL</option>
                        </select>
                      )}
                    </Field>
                    {restoreDatabaseType === "Postgresql" && (
                      <Field label="backups.connectionReference">
                        {(props) => (
                          <input
                            {...props}
                            value={restoreConnectionEnvironmentVariable}
                            required
                            pattern="[A-Za-z_][A-Za-z0-9_]{0,127}"
                            maxLength={128}
                            onChange={(event) =>
                              setRestoreConnectionEnvironmentVariable(
                                event.target.value,
                              )
                            }
                          />
                        )}
                      </Field>
                    )}
                    <Field label="backups.port">
                      {(props) => (
                        <input
                          {...props}
                          type="number"
                          min={1024}
                          max={65535}
                          value={port}
                          required
                          onChange={(event) =>
                            setPort(Number(event.target.value))
                          }
                        />
                      )}
                    </Field>
                  </>
                )}
                <Field label="backups.password">
                  {(props) => (
                    <input
                      {...props}
                      type="password"
                      autoComplete="off"
                      value={password}
                      minLength={12}
                      required={includeSecrets || dialog.artifact?.encrypted}
                      onChange={(event) => setPassword(event.target.value)}
                    />
                  )}
                </Field>
                {dialog.kind === "create" && (
                  <label
                    className="checkbox-label"
                    title={t("backups.secrets.help")}
                  >
                    <input
                      type="checkbox"
                      title={t("backups.secrets.help")}
                      checked={includeSecrets}
                      onChange={(event) =>
                        setIncludeSecrets(event.target.checked)
                      }
                    />
                    {t("backups.secrets")}
                  </label>
                )}
              </>
            )}
            {failure && <ErrorPanel error={failure} />}
            <div className="form-actions">
              <Button
                label="common.cancel"
                onClick={close}
                type="button"
                disabled={busy}
              />
              <Button
                label={`backups.${dialog.kind}`}
                type="submit"
                disabled={busy}
              />
            </div>
          </form>
        </Modal>
      )}
    </>
  );
}
