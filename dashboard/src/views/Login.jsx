import brandIcon from "../../../icon.png";
import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { api } from "../api/client.js";
import { signIn, signInWithToken } from "../api/session.js";
import { Language, ThemeControl } from "../components/Chrome.jsx";
import { Button, ErrorPanel, Field } from "../components/ui.jsx";

/** Where to go after signing in: the page that sent the user here, if it is a dashboard page. */
export function returnPath(location) {
  const from = location.state?.from || new URLSearchParams(location.search).get("return") || "";
  return from.startsWith("/dashboard/") && !from.startsWith("/dashboard/login") ? from : "/dashboard/home";
}

/** Sign-in page: email and password, an optional tenant choice, or a bearer token. Always shows the default credentials. */
export default function Login({ theme, toggleTheme, onSignedIn }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [info, setInfo] = useState(null);
  const [mode, setMode] = useState("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [token, setToken] = useState("");
  const [tenants, setTenants] = useState([]);
  const [tenantId, setTenantId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(location.state?.expired ? { message: t("login.expired") } : null);
  useEffect(() => { api.loginInfo().then(setInfo).catch(() => setInfo(null)); }, []);
  const defaults = { email: info?.defaultEmail || "admin@arguebot", password: info?.defaultPassword || "password", token: info?.defaultToken || "arguebotadmin" };
  const finish = (value) => { onSignedIn(value); navigate(returnPath(location), { replace: true }); };
  const submit = async (event) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true); setError(null);
    try {
      if (mode === "token") finish(await signInWithToken(token));
      else finish(await signIn(email, password, tenantId));
    } catch (failure) {
      if (failure.code === "tenant_required") {
        setTenants(failure.tenants || []);
        setTenantId("");
        setError({ message: t("login.tenantRequired") });
      } else setError(failure);
    } finally { setBusy(false); }
  };
  return (
    <main className="login">
      <div className="login-utilities"><ThemeControl theme={theme} toggle={toggleTheme} /><Language /></div>
      <section className="login-card">
        <div className="wordmark"><img src={brandIcon} alt="" width="36" height="36" />{t("app.name")}</div>
        <h1>{t("login.title")}</h1>
        <p>{t("login.description")}</p>
        <form onSubmit={submit} noValidate>
          {mode === "password" ? <>
            <Field label="login.email">{(props) => <input {...props} type="email" autoComplete="username" data-autofocus autoFocus required value={email} onChange={(event) => { setEmail(event.target.value); setTenants([]); }} />}</Field>
            <Field label="login.password">{(props) => <input {...props} type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />}</Field>
            {tenants.length > 0 && <Field label="login.tenant">{(props) => <select {...props} required value={tenantId} onChange={(event) => setTenantId(event.target.value)}>
              <option value="" disabled>{t("login.tenant.choose")}</option>
              {tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
            </select>}</Field>}
          </> : <Field label="login.token">{(props) => <input {...props} type="password" autoComplete="off" spellCheck="false" autoFocus required value={token} onChange={(event) => setToken(event.target.value)} />}</Field>}
          {error && <ErrorPanel error={error} />}
          <Button label="login.submit" type="submit" className="primary" disabled={busy || (mode === "token" ? !token.trim() : !email.trim() || !password || (tenants.length > 0 && !tenantId))} />
          <Button label={mode === "token" ? "login.usePassword" : "login.useToken"} type="button" className="link-button" onClick={() => { setMode(mode === "token" ? "password" : "token"); setError(null); setTenants([]); }} />
        </form>
        <aside className="default-credentials" aria-label={t("login.defaults.title")}>
          <p>{t("login.defaults.title")}: <code>{defaults.email}</code> / <code>{defaults.password}</code></p>
          <p>{t("login.defaults.token")}: <code>{defaults.token}</code></p>
        </aside>
        {info?.version && <p className="login-version"><code>{info.version}</code></p>}
      </section>
    </main>
  );
}
