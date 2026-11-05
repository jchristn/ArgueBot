import ActionMenu from "../components/ActionMenu.jsx";
import CatalogTab, { catalogKinds } from "../components/CatalogTab.jsx";
import Tabs from "../components/Tabs.jsx";
import ObjectRow from "../components/ObjectRow.jsx";
import CopyableId from "../components/CopyableId.jsx";
import ModelChecksDialog from "../components/ModelChecksDialog.jsx";
import harnesses from "../../../assets/harness-defaults.json";

/** The stored secret kind that signs in each CLI program; other programs read keys from allowed environment variables. */
const secretKinds = { claude: "claude_oauth", codex: "codex_auth_json" };
import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field, Modal } from "../components/ui.jsx";
function ProfileForm({ onSave, onClose, initial, api }) {
  const { t } = useTranslation();
  const [profile, setProfile] = useState(
    initial ? { ...initial, ...(initial.kind === "cli" ? { executable: initial.executable || harnesses[initial.provider]?.executable || "", arguments: initial.arguments.length ? initial.arguments : [...(harnesses[initial.provider]?.arguments || [])] } : {}) } : {
      name: "Claude Code",
      kind: "cli",
      provider: "claude",
      executable: harnesses.claude.executable,
      arguments: [...harnesses.claude.arguments],
      workingDirectory: "",
      endpoint: "http://127.0.0.1:11434",
      model: "",
      secretEnvironmentVariable: "",
      credentialId: "",
      restrictEnvironment: true,
      environmentVariables: [],
      timeoutSeconds: 300,
      maxOutputBytes: 67108864,
      enabled: true,
    },
  );
  const { data: models } = useData("models", () => api.configuration("models"));
  const { data: secrets } = useData("credentials", () => api.configuration("credentials"));
  const secretKind = profile.kind === "cli" ? secretKinds[profile.provider] || "" : "";
  const usableSecrets = (secrets || []).filter(item => item.kind === secretKind && (item.status === "ready" || item.id === profile.credentialId));
  const [busy, setBusy] = useState(false);
  const executableIsCommand = profile.kind === "cli" && /^\s*(claude|codex|gemini|mux)(\.exe)?\s+-/i.test(profile.executable);
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
    <Modal title={initial?.id ? "participants.edit" : "participants.add"} onClose={onClose} wide>
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          if (executableIsCommand) return;
          setBusy(true);
          try {
            const cli = profile.kind === "cli" && !profile.modelProfileId;
            const environmentVariables = cli ? (profile.environmentVariables || []).map(name => name.trim()).filter(Boolean) : [];
            await onSave({
              ...profile,
              model: (profile.model || "").trim(),
              credentialId: cli && secretKind ? profile.credentialId || "" : "",
              environmentVariables,
              restrictEnvironment: cli && (!initial?.id || Boolean(initial.restrictEnvironment) || environmentVariables.length > 0),
            });
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
          <Field label="catalog.modelProfileId">{props => <select {...props} value={profile.modelProfileId || ""} onChange={event => setProfile(value => ({ ...value, modelProfileId: event.target.value }))}><option value="">{t("catalog.inline")}</option>{models?.map(model => <option key={model.id} value={model.id}>{model.name}</option>)}</select>}</Field>
          <Field label="catalog.roleInstructions">{props => <textarea {...props} value={profile.roleInstructions || ""} rows={3} onChange={event => setProfile(value => ({ ...value, roleInstructions: event.target.value }))} />}</Field>
          {!profile.modelProfileId && <Field label="profile.kind">
            {(props) => (
              <select
                {...props}
                value={profile.kind === "rest" ? "rest" : profile.provider}
                onChange={(event) => {
                  const choice = event.target.value;
                  setProfile((value) => choice === "rest"
                    ? { ...value, kind: "rest", provider: "ollama", arguments: [], executable: "" }
                    : { ...value, kind: "cli", provider: choice, credentialId: secretKinds[choice] === secretKinds[value.provider] ? value.credentialId : "", executable: harnesses[choice].executable, arguments: [...harnesses[choice].arguments], name: !value.name || Object.values(harnesses).some((preset) => preset.name === value.name) ? harnesses[choice].name : value.name });
                }}
              >
                {Object.entries(harnesses).map(([provider, preset]) => <option key={provider} value={provider}>{preset.name}</option>)}
                <option value="rest">{t("profile.rest")}</option>
              </select>
            )}
          </Field>
          }
          {!profile.modelProfileId && profile.kind === "rest" && <Field label="profile.provider">
            {(props) => <select {...props} value={profile.provider} onChange={(event) => setProfile((value) => ({ ...value, provider: event.target.value }))}>
              {["ollama", "openai", "anthropic", "gemini"].map((provider) => <option key={provider} value={provider}>{provider}</option>)}
            </select>}
          </Field>}
          {!profile.modelProfileId && (profile.kind === "cli" ? (
            <>
              <div>
              {field("executable", "profile.executable", "text", {
                required: true,
                placeholder: "claude",
                "aria-invalid": executableIsCommand || undefined,
              })}
              {executableIsCommand && <p role="alert">{t("profile.executable.commandError")}</p>}
              </div>
              {field("workingDirectory", "profile.directory")}
              <fieldset className="argument-editor">
                <legend title={t("profile.arguments.help")}>
                  {t("profile.arguments")}
                </legend>
                <p className="muted">{t("profile.arguments.help")}</p>
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
              <Field label={["claude", "codex", "gemini"].includes(profile.provider) ? "profile.cliModel" : "profile.cliModel.placeholder"}>{props => <input {...props} value={profile.model || ""} maxLength={1000} placeholder={profile.provider === "claude" ? "sonnet" : profile.provider === "codex" ? "gpt-5.5" : ""} onChange={event => setProfile(value => ({ ...value, model: event.target.value }))} />}</Field>
              {secretKind ? <div>
                <Field label="profile.credential">{props => <select {...props} value={profile.credentialId || ""} onChange={event => setProfile(value => ({ ...value, credentialId: event.target.value }))}><option value="">{t("profile.credential.none")}</option>{usableSecrets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>}</Field>
                {usableSecrets.length === 0 && <p className="muted field-note">{t("profile.credential.addFirst", { kind: t(`credential.kind.${secretKind}`) })}</p>}
              </div> : <p className="muted field-note">{t("profile.credential.unsupported")}</p>}
              <Field label="catalog.environmentVariables">{props => <input {...props} value={(profile.environmentVariables || []).join(", ")} onChange={event => setProfile(value => ({ ...value, environmentVariables: event.target.value.split(",").map(name => name.trimStart()) }))} />}</Field>
            </>
          ) : (
            <>
              {field("endpoint", "profile.endpoint", "url", { required: true })}
              {field("workingDirectory", "profile.directory")}
              {field("model", "profile.model", "text", { required: true })}
              {field("secretEnvironmentVariable", "profile.secret", "text", {
                autoComplete: "off",
              })}
            </>
          ))}
          {field("timeoutSeconds", "profile.timeout", "number", {
            min: 1,
            max: 3600,
          })}
          {field("maxOutputBytes", "profile.maxOutput", "number", {
            min: 1024,
            max: 67108864,
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
        <p className="muted">{t("profile.snapshots.help")}</p>
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
            disabled={busy || executableIsCommand}
          />
        </footer>
      </form>
    </Modal>
  );
}
export default function Participants() {
  const { api, notify, session } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, reload } = useData("participants", () =>
    api.participants(),
  );
  const catalog = useData("catalog", async () => Object.fromEntries(await Promise.all(catalogKinds.map(async key => [key, await api.configuration(key)]))));
  const [modal, setModal] = useState(null);
  const secretName = id => catalog.data?.credentials?.find(item => item.id === id)?.name || id;
  const modelName = id => catalog.data?.models?.find(item => item.id === id)?.name;
  const agents = () => (
    <>
      <div className="tab-toolbar">
        <p className="muted">{t("participants.description")}</p>
        <div className="toolbar-actions">
          <Button label="common.refresh" icon="refresh" iconOnly onClick={reload} />
          <Button label="participants.add" icon="plus" className="primary" onClick={() => setModal({ kind: "form" })} />
        </div>
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th scope="col">{t("profile.nameColumn")}</th>
              <th scope="col">{t("common.identifier")}</th>
              <th scope="col">{t("profile.typeColumn")}</th>
              <th scope="col">{t("profile.modelColumn")}</th>
              <th scope="col">{t("profile.secretColumn")}</th>
              <th scope="col">{t("profile.enabledColumn")}</th>
              <th scope="col" className="actions-cell">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {data?.map((profile) => (
              <ObjectRow key={profile.id} onOpen={() => setModal({ kind: "form", profile })}>
                <td>
                  <strong>{profile.name}</strong>
                </td>
                <td><CopyableId value={profile.id} /></td>
                <td>
                  {profile.modelProfileId ? t("profile.runsModel", { name: modelName(profile.modelProfileId) || profile.modelProfileId }) : profile.kind === "rest" ? `HTTP · ${profile.provider}` : harnesses[profile.provider]?.name || profile.executable}
                </td>
                <td>
                  {profile.model ? <code>{profile.model}</code> : <span className="muted">{t("profile.defaultModel")}</span>}
                </td>
                <td>
                  {profile.credentialId ? secretName(profile.credentialId) : profile.kind === "cli" && !profile.modelProfileId ? <span className="muted">{t("catalog.localLogin")}</span> : <span className="muted">{t("profile.noSecret")}</span>}
                </td>
                <td>
                  <span className="badge">
                    {t(profile.enabled ? "profile.yes" : "profile.no")}
                  </span>
                </td>
                <td className="actions-cell">
                  <ActionMenu context={profile.name} items={[
                    { label: "common.edit", help: "participants.add.help", onClick: () => setModal({ kind: "form", profile }) },
                    { label: "agentCheck.run", help: "agentCheck.help", disabled: !profile.enabled, onClick: () => setModal({ kind: "check", profile }) },
                    { label: "common.inspect", help: "common.inspect", onClick: () => setModal({ kind: "json", profile }) },
                    { label: "common.delete", help: "profile.delete.help", danger: true, onClick: () => setModal({ kind: "delete", profile }) },
                  ]} />
                </td>
              </ObjectRow>
            ))}
          </tbody>
        </table>
      </div>
      {data?.length === 0 && (
        <div className="empty-state">{t("participants.empty")}</div>
      )}
    </>
  );
  const tabs = [
    { key: "agents", label: "participants.tab.agents", help: "participants.tab.agents.help", count: data?.length, render: agents },
    ...catalogKinds.map(kind => ({ key: kind, label: `catalog.${kind}`, help: `catalog.${kind}.help`, count: catalog.data?.[kind]?.length, render: () => <CatalogTab api={api} notify={notify} kind={kind} data={catalog.data && { ...catalog.data, agents: data || [] }} error={catalog.error} reload={catalog.reload} systemAdmin={!!session?.user?.isAdmin} /> })),
  ];
  return (
    <>
      <div className="page-heading">
        <div>
          <h1>{t("participants.title")}</h1>
          <p>{t("participants.pageDescription")}</p>
        </div>
      </div>
      <Tabs tabs={tabs} label="participants.title" />
      {modal?.kind === "form" && (
        <ProfileForm
          api={api}
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
      {modal?.kind === "check" && <ModelChecksDialog api={api} model={modal.profile} notify={notify} agent onClose={() => setModal(null)} />}
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
