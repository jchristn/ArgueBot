import { useState } from "react";
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
import { passwordDigest } from "../api/client.js";

const empty = () => ({
  name: "",
  email: "",
  role: "viewer",
  isAdmin: false,
  isTenantAdmin: false,
  active: true,
  password: "",
  expectedRevision: 0,
});
export default function Users() {
  const { api, user } = useOutletContext();
  const { t } = useTranslation();
  const [filter, setFilter] = useState({
    search: "",
    pageNumber: 1,
    pageSize: 25,
  });
  const [draft, setDraft] = useState(null);
  const [failure, setFailure] = useState(null);
  const [busy, setBusy] = useState(false);
  const { data, error, loading, reload } = useData(JSON.stringify(filter), () =>
    api.request("/v1.0/api/users?" + new URLSearchParams(filter)),
  );
  const change = (values) => setDraft((current) => ({ ...current, ...values }));
  async function save(event) {
    event.preventDefault();
    setBusy(true);
    setFailure(null);
    try {
      const { id, password, ...body } = draft;
      if (password) body.passwordSha256 = await passwordDigest(password);
      await api.request(
        "/v1.0/api/users" + (id ? "/" + encodeURIComponent(id) : ""),
        { method: id ? "PUT" : "POST", body },
      );
      setDraft(null);
      reload();
    } catch (error) {
      setFailure(error);
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <h1 title={t("users.description")}>{t("users.title")}</h1>
          <p>{t("users.description")}</p>
        </div>
        <Button
          label="users.create"
          icon="plus"
          onClick={() => {
            setDraft(empty());
            setFailure(null);
          }}
        />
      </div>
      <div className="filters">
        <Field label="users.search">
          {(props) => (
            <input
              {...props}
              type="search"
              maxLength={200}
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
      </div>
      {error && <ErrorPanel error={error} retry={reload} />}
      {loading && <p role="status">{t("common.loading")}</p>}
      {data && (
        <>
          <Pagination
            page={data}
            onChange={(values) => setFilter({ ...filter, ...values })}
          />
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  {[
                    "users.name",
                    "users.email",
                    "users.role",
                    "users.active",
                    "common.actions",
                  ].map((key) => (
                    <th key={key} title={t(key)} scope="col">
                      {t(key)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.items.map((entry) => (
                  <tr key={entry.id}>
                    <td title={entry.id}>{entry.name}</td>
                    <td title={t("users.email.help")}>
                      {entry.email || t("common.none")}
                    </td>
                    <td title={t("users.flags.help")}>
                      {entry.isAdmin
                        ? t("users.isAdmin")
                        : entry.isTenantAdmin
                          ? t("users.isTenantAdmin")
                          : t("users.role." + entry.role)}
                    </td>
                    <td title={t("users.active.help")}>
                      {t(entry.active ? "users.enabled" : "users.disabled")}
                    </td>
                    <td>
                      {entry.protected ? (
                        <span title={t("users.protected.help")}>
                          {t("users.protected")}
                        </span>
                      ) : (
                        <Button
                          label="users.edit"
                          disabled={!user.isAdmin && entry.isAdmin}
                          onClick={() => {
                            setDraft({
                              ...empty(),
                              id: entry.id,
                              name: entry.name,
                              email: entry.email,
                              role: entry.role,
                              isAdmin: entry.isAdmin,
                              isTenantAdmin: entry.isTenantAdmin,
                              active: entry.active,
                              expectedRevision: entry.revision,
                            });
                            setFailure(null);
                          }}
                        />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {data.items.length === 0 && (
            <p className="empty-state">{t("users.empty")}</p>
          )}
        </>
      )}
      {draft && (
        <Modal
          title={draft.id ? "users.edit" : "users.create"}
          onClose={() => !busy && setDraft(null)}
        >
          <form onSubmit={save}>
            <Field label="users.name">
              {(props) => (
                <input
                  {...props}
                  required
                  maxLength={200}
                  value={draft.name}
                  onChange={(event) => change({ name: event.target.value })}
                />
              )}
            </Field>
            <Field label="users.email">
              {(props) => (
                <input
                  {...props}
                  type="email"
                  required
                  maxLength={254}
                  value={draft.email}
                  onChange={(event) => change({ email: event.target.value })}
                />
              )}
            </Field>
            <Field label="users.role">
              {(props) => (
                <select
                  {...props}
                  value={draft.role}
                  onChange={(event) => change({ role: event.target.value })}
                >
                  {["viewer", "operator", "admin"].map((role) => (
                    <option
                      key={role}
                      value={role}
                      title={t("users.role." + role)}
                    >
                      {t("users.role." + role)}
                    </option>
                  ))}
                </select>
              )}
            </Field>
            {[
              "active",
              "isTenantAdmin",
              ...(user.isAdmin ? ["isAdmin"] : []),
            ].map((field) => (
              <Field key={field} label={"users." + field}>
                {(props) => (
                  <input
                    {...props}
                    type="checkbox"
                    checked={draft[field]}
                    onChange={(event) =>
                      change({ [field]: event.target.checked })
                    }
                  />
                )}
              </Field>
            ))}
            <Field label="users.password">
              {(props) => (
                <input
                  {...props}
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  maxLength={1024}
                  value={draft.password}
                  onChange={(event) => change({ password: event.target.value })}
                />
              )}
            </Field>
            {failure && <ErrorPanel error={failure} />}
            <div className="actions">
              <Button label="common.save" type="submit" disabled={busy} />
              <Button
                label="common.cancel"
                type="button"
                disabled={busy}
                onClick={() => setDraft(null)}
              />
            </div>
          </form>
        </Modal>
      )}
    </>
  );
}
