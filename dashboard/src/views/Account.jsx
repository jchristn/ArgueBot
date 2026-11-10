import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { updateSessionUser } from "../api/session.js";
import AuthCredentials from "../components/AuthCredentials.jsx";
import AuthSessions from "../components/AuthSessions.jsx";
import QuestionNotifications from "../components/QuestionNotifications.jsx";
import { useData } from "../components/useData.js";
import { Button, ErrorPanel, Field } from "../components/ui.jsx";

/** The signed-in user's profile, password, bearer-token credentials and sessions. */
export default function Account() {
  const { api, notify, session } = useOutletContext();
  const { t } = useTranslation();
  const { data, error, reload } = useData("me", () => api.me());
  const [profile, setProfile] = useState(null);
  const [profileError, setProfileError] = useState(null);
  const [password, setPassword] = useState({ current: "", next: "", confirm: "" });
  const [passwordError, setPasswordError] = useState(null);
  const [passwordDone, setPasswordDone] = useState(false);
  const value = profile || data;
  const credentialSession = session?.principalType === "Credential";
  return <>
    <div className="page-heading"><div><h1>{t("account.title")}</h1><p>{t("account.description", { tenant: session?.tenant?.name || "" })}</p></div></div>
    {error && <ErrorPanel error={error} retry={reload} />}
    {value && <section className="page-section">
      <header><h2>{t("account.profile")}</h2></header>
      <form className="account-form" onSubmit={async event => {
        event.preventDefault(); setProfileError(null);
        try { const saved = await api.updateMe({ firstName: value.firstName, lastName: value.lastName, email: value.email, revision: value.revision }); setProfile(null); updateSessionUser(saved); await reload(); }
        catch (problem) { setProfileError(problem); }
      }}>
        <div className="form-grid">
          <Field label="account.firstName">{props => <input {...props} maxLength={128} autoComplete="given-name" value={value.firstName} onChange={event => setProfile({ ...value, firstName: event.target.value })} />}</Field>
          <Field label="account.lastName">{props => <input {...props} maxLength={128} autoComplete="family-name" value={value.lastName} onChange={event => setProfile({ ...value, lastName: event.target.value })} />}</Field>
        </div>
        <Field label="account.email">{props => <input {...props} type="email" required maxLength={256} autoComplete="email" value={value.email} onChange={event => setProfile({ ...value, email: event.target.value })} />}</Field>
        <p className="muted">{t(value.isAdmin ? "account.role.system" : value.isTenantAdmin ? "account.role.tenant" : "account.role.member")}</p>
        {profileError && <ErrorPanel error={profileError} />}
        <footer className="dialog-actions"><Button label="common.save" type="submit" className="primary" disabled={!profile} /></footer>
      </form>
    </section>}
    <QuestionNotifications />
    <section className="page-section">
      <header><h2>{t("account.password")}</h2></header>
      <form className="account-form" onSubmit={async event => {
        event.preventDefault(); setPasswordError(null); setPasswordDone(false);
        if (password.next !== password.confirm) { setPasswordError({ message: t("account.password.mismatch") }); return; }
        try { await api.changePassword(password.current, password.next); setPassword({ current: "", next: "", confirm: "" }); setPasswordDone(true); }
        catch (problem) { setPasswordError(problem); }
      }}>
        <Field label="account.password.current">{props => <input {...props} type="password" autoComplete="current-password" required value={password.current} onChange={event => setPassword({ ...password, current: event.target.value })} />}</Field>
        <div className="form-grid">
          <Field label="account.password.new">{props => <input {...props} type="password" autoComplete="new-password" required minLength={8} value={password.next} onChange={event => setPassword({ ...password, next: event.target.value })} />}</Field>
          <Field label="account.password.confirm">{props => <input {...props} type="password" autoComplete="new-password" required minLength={8} value={password.confirm} onChange={event => setPassword({ ...password, confirm: event.target.value })} />}</Field>
        </div>
        {passwordError && <ErrorPanel error={passwordError} />}
        {passwordDone && <p className="info-banner" role="status">{t("account.password.changed")}</p>}
        <footer className="dialog-actions"><Button label="account.password.change" type="submit" className="primary" disabled={!password.current || password.next.length < 8} /></footer>
      </form>
    </section>
    <AuthCredentials api={api} notify={notify} />
    {credentialSession ? <section className="page-section"><header><h2>{t("authSessions.title")}</h2></header><p className="muted">{t("account.credentialSession")}</p></section> : <AuthSessions api={api} notify={notify} />}
  </>;
}
