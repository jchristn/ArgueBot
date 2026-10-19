import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
function ProfileForm({ onSave, onClose, initial }) {
  const { t } = useTranslation();
  const [profile, setProfile] = useState(
    initial || {
      name: "",
      kind: "cli",
      provider: "plain",
      executable: "",
      arguments: [],
      workingDirectory: "",
      endpoint: "http://127.0.0.1:11434",
      model: "",
      secretEnvironmentVariable: "",
      timeoutSeconds: 300,
      maxOutputBytes: 1048576,
      enabled: true,
    },
  );
  const [busy, setBusy] = useState(false);
  const field = (name, label, type = "text", extra = {}) => (
    <Field label={label}>
      {(props) => (
        <input
          {...props}
          {...extra}
          type={type}
          value={profile[name]}
          onChange={(event) =>
            setProfile((value) => ({
              ...value,
              [name]:
                type === "number"
                  ? Number(event.target.value)
                  : event.target.value,
            }))
          }
        />
      )}
    </Field>
  );
  return (
    <Modal title="participants.add" onClose={onClose} wide>
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);
          try {
            await onSave(profile);
          } finally {
            setBusy(false);
          }
        }}
      >
        <div className="form-grid">
          {field("name", "profile.name", "text", {
            required: true,
            maxLength: 200,
          })}
          <Field label="profile.kind">
            {(props) => (
              <select
                {...props}
                value={profile.kind}
                onChange={(event) =>
                  setProfile((value) => ({
                    ...value,
                    kind: event.target.value,
                    provider: event.target.value === "cli" ? "plain" : "ollama",
                  }))
                }
              >
                <option value="cli">{t("profile.cli")}</option>
                <option value="rest">{t("profile.rest")}</option>
              </select>
            )}
          </Field>
          <Field label="profile.provider">
            {(props) => (
              <select
                {...props}
                value={profile.provider}
                onChange={(event) =>
                  setProfile((value) => ({
                    ...value,
                    provider: event.target.value,
                  }))
                }
              >
                {(profile.kind === "cli"
                  ? ["plain", "claude", "codex", "gemini"]
                  : ["ollama", "openai", "anthropic", "gemini"]
                ).map((provider) => (
                  <option key={provider} value={provider}>
                    {provider}
                  </option>
                ))}
              </select>
            )}
          </Field>
          {profile.kind === "cli" ? (
            <>
              {field("executable", "profile.executable", "text", {
                required: true,
              })}
              {field("workingDirectory", "profile.directory")}
              <fieldset className="argument-editor">
                <legend title={t("profile.arguments.help")}>
                  {t("profile.arguments")}
                </legend>
                {profile.arguments.map((argument, index) => (
                  <div className="argument-row" key={index}>
                    <input
                      aria-label={t("profile.argument", { number: index + 1 })}
                      title={t("profile.argument.help")}
                      value={argument}
                      onChange={(event) =>
                        setProfile((value) => ({
                          ...value,
                          arguments: value.arguments.map((item, position) =>
                            position === index ? event.target.value : item,
                          ),
                        }))
                      }
                    />
                    <Button
                      label="profile.argument.remove"
                      icon="close"
                      iconOnly
                      type="button"
                      onClick={() =>
                        setProfile((value) => ({
                          ...value,
                          arguments: value.arguments.filter(
                            (_, position) => position !== index,
                          ),
                        }))
                      }
                    />
                  </div>
                ))}
                <Button
                  label="profile.argument.add"
                  icon="plus"
                  type="button"
                  onClick={() =>
                    setProfile((value) => ({
                      ...value,
                      arguments: [...value.arguments, ""],
                    }))
                  }
                />
              </fieldset>
            </>
          ) : (
            <>
              {field("endpoint", "profile.endpoint", "url", { required: true })}
              {field("model", "profile.model", "text", { required: true })}
              {field("secretEnvironmentVariable", "profile.secret", "text", {
                autoComplete: "off",
              })}
            </>
          )}
          {field("timeoutSeconds", "profile.timeout", "number", {
            min: 1,
            max: 3600,
          })}
          {field("maxOutputBytes", "profile.maxOutput", "number", {
            min: 1024,
            max: 16777216,
          })}
        </div>
        <label className="checkbox-label" title={t("profile.enabled.help")}>
          <input
            type="checkbox"
            title={t("profile.enabled.help")}
            checked={profile.enabled}
            onChange={(event) =>
              setProfile((value) => ({
                ...value,
                enabled: event.target.checked,
              }))
            }
          />
          {t("profile.enabled")}
        </label>
        <p className="muted">{t("profile.capability")}</p>
        <footer>
          <Button
            label="common.cancel"
            help="common.cancel"
            type="button"
            onClick={onClose}
          />
          <Button
            label="common.save"
            help="participants.add.help"
            className="primary"
            type="submit"
            disabled={busy}
          />
        </footer>
      </form>
    </Modal>
  );
}
export default function Participants() {
  const { api, notify } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, reload } = useData("participants", () =>
    api.participants(),
  );
  const [modal, setModal] = useState(null);
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("participants.title")}</h1>
          <p>{t("participants.description")}</p>
        </div>
        <Button
          label="participants.add"
          icon="plus"
          className="primary"
          onClick={() => setModal({ kind: "form" })}
        />
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      <div className="filters">
        <Button
          label="common.refresh"
          icon="refresh"
          iconOnly
          onClick={reload}
        />
      </div>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th scope="col">{t("profile.nameColumn")}</th>
              <th scope="col">{t("profile.typeColumn")}</th>
              <th scope="col">{t("profile.modelColumn")}</th>
              <th scope="col">{t("profile.enabledColumn")}</th>
              <th scope="col">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {data?.map((profile) => (
              <tr key={profile.id}>
                <td>
                  <strong>{profile.name}</strong>
                </td>
                <td>
                  {profile.kind} · {profile.provider}
                </td>
                <td>
                  <code>{profile.model || profile.executable}</code>
                </td>
                <td>
                  <span className="badge">
                    {t(profile.enabled ? "profile.yes" : "profile.no")}
                  </span>
                </td>
                <td>
                  <div className="row-actions">
                    <Button
                      label="common.edit"
                      help="participants.add.help"
                      onClick={() => setModal({ kind: "form", profile })}
                    />
                    <Button
                      label="common.inspect"
                      help="common.inspect"
                      onClick={() => setModal({ kind: "json", profile })}
                    />
                    <Button
                      label="common.delete"
                      help="profile.delete.help"
                      className="danger"
                      onClick={() => setModal({ kind: "delete", profile })}
                    />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data?.length === 0 && (
        <div className="empty-state">{t("participants.empty")}</div>
      )}
      {modal?.kind === "form" && (
        <ProfileForm
          initial={modal.profile}
          onClose={() => setModal(null)}
          onSave={async (profile) => {
            try {
              if (profile.id) await api.updateParticipant(profile);
              else await api.createParticipant(profile);
              setModal(null);
              reload();
            } catch (failure) {
              notify(failure);
            }
          }}
        />
      )}{" "}
      {modal?.kind === "json" && (
        <Modal title="common.inspect" onClose={() => setModal(null)}>
          <pre>{JSON.stringify(modal.profile, null, 2)}</pre>
        </Modal>
      )}{" "}
      {modal?.kind === "delete" && (
        <Modal title="common.delete" onClose={() => setModal(null)}>
          <p>{t("profile.delete.confirm")}</p>
          <footer>
            <Button
              label="common.cancel"
              help="common.cancel"
              onClick={() => setModal(null)}
            />
            <Button
              label="common.delete"
              help="profile.delete.help"
              className="danger"
              onClick={async () => {
                try {
                  await api.deleteParticipant(modal.profile.id);
                  setModal(null);
                  reload();
                } catch (failure) {
                  notify(failure);
                }
              }}
            />
          </footer>
        </Modal>
      )}
    </>
  );
}
