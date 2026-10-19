import { lazy, Suspense, useCallback, useEffect, useState } from "react";
import {
  BrowserRouter,
  Link,
  NavLink,
  Navigate,
  Outlet,
  Route,
  Routes,
} from "react-router-dom";
import { useTranslation } from "react-i18next";
import { api } from "./api/client.js";
import i18n, { registry } from "./i18n/index.js";
import { Button, ErrorPanel, Field, Icon } from "./components/ui.jsx";
const Home = lazy(() => import("./views/Home.jsx"));
const Conversations = lazy(() => import("./views/Conversations.jsx"));
const Workspace = lazy(() => import("./views/Workspace.jsx"));
const Participants = lazy(() => import("./views/Participants.jsx"));
const RequestHistory = lazy(() => import("./views/RequestHistory.jsx"));
const ApiExplorer = lazy(() => import("./views/ApiExplorer.jsx"));
const Backups = lazy(() => import("./views/Backups.jsx"));
const Settings = lazy(() => import("./views/Settings.jsx"));
function Language() {
  const { t } = useTranslation();
  return (
    <select
      className="language"
      aria-label={t("nav.language")}
      title={t("nav.language")}
      value={i18n.language}
      onChange={(event) => i18n.changeLanguage(event.target.value)}
    >
      {registry.map((locale) => (
        <option key={locale.code} value={locale.code}>
          {locale.nativeName}
        </option>
      ))}
    </select>
  );
}
function ThemeControl({ theme, toggle }) {
  const { t } = useTranslation();
  const label = t("nav.theme", { theme: theme === "dark" ? "light" : "dark" });
  return (
    <button
      className="button icon-button"
      onClick={toggle}
      title={label}
      aria-label={label}
    >
      <Icon name={theme === "dark" ? "sun" : "moon"} />
    </button>
  );
}
function Login({ onLogin, theme, toggleTheme }) {
  const { t } = useTranslation();
  const [code, setCode] = useState("");
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  return (
    <main className="login">
      <div className="login-utilities">
        <ThemeControl theme={theme} toggle={toggleTheme} />
        <Language />
      </div>
      <section className="login-card">
        <div className="wordmark">
          <Icon name="chat" />
          {t("app.name")}
        </div>
        <h1>{t("login.title")}</h1>
        <p>{t("login.description")}</p>
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            setBusy(true);
            setError(null);
            try {
              onLogin(await api.pair(code.trim()));
            } catch (failure) {
              setError(failure);
            } finally {
              setBusy(false);
            }
          }}
        >
          <Field label="login.code">
            {(props) => (
              <input
                {...props}
                type="password"
                autoComplete="one-time-code"
                required
                value={code}
                onChange={(event) => setCode(event.target.value)}
              />
            )}
          </Field>
          {error && <ErrorPanel error={error} />}
          <Button
            label="login.submit"
            className="primary"
            type="submit"
            disabled={busy || !code.trim()}
          />
        </form>
        <p className="muted">{t("login.where")}</p>
        <code title={t("login.server.help")}>{location.origin}</code>
      </section>
    </main>
  );
}
function Shell({ user, onLogout, theme, toggleTheme, notify }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const groups = [
    {
      title: "nav.operate",
      links: [
        ["home", "nav.home", "grid"],
        ["conversations", "nav.conversations", "chat"],
      ],
    },
    {
      title: "nav.configure",
      links: [["participants", "nav.participants", "users"]],
    },
    {
      title: "nav.system",
      links: [
        ["history", "history.title", "chat"],
        ["explorer", "explorer.title", "grid"],
        ["settings", "nav.settings", "settings"],
        ["backups", "backups.title", "download"],
      ],
    },
  ];
  return (
    <div className="app-shell">
      <aside className={open ? "sidebar open" : "sidebar"}>
        <Link
          to="/dashboard/home"
          className="wordmark"
          title={t("app.tagline")}
        >
          <Icon name="chat" />
          {t("app.name")}
        </Link>
        <nav aria-label={t("nav.operate")}>
          {groups.map((group) => (
            <section key={group.title}>
              <h2>{t(group.title)}</h2>
              {group.links.map(([path, label, icon]) => (
                <NavLink
                  key={path}
                  to={"/dashboard/" + path}
                  title={t(label)}
                  onClick={() => setOpen(false)}
                >
                  <Icon name={icon} />
                  {t(label)}
                </NavLink>
              ))}
            </section>
          ))}
        </nav>
        <div className="sidebar-footer">
          <span className="status-dot" />
          <span>{t("common.connected")}</span>
          <code>0.1.0</code>
        </div>
      </aside>
      <div className="app-main">
        <header className="topbar">
          <Button
            label="nav.menu"
            help="nav.menu"
            icon="menu"
            iconOnly
            className="mobile-menu"
            onClick={() => setOpen(!open)}
          />
          <div className="server-context">
            <span title={t("login.server.help")}>{location.host}</span>
            <span className="identity" title={user.tenantId}>
              {user.userId}
            </span>
            <span className="badge" title={user.role}>
              {user.role}
            </span>
          </div>
          <div className="utilities">
            <a
              className="button icon-button"
              href="https://github.com/jchristn/ArgueBot"
              target="_blank"
              rel="noreferrer"
              title={t("nav.repository")}
              aria-label={t("nav.repository")}
            >
              <Icon name="external" />
            </a>
            <ThemeControl theme={theme} toggle={toggleTheme} />
            <Language />
            <Button
              label="nav.logout"
              help="nav.logout"
              icon="logout"
              iconOnly
              onClick={onLogout}
            />
          </div>
        </header>
        <div className="route-content">
          <Outlet context={{ api, user, notify }} />
        </div>
      </div>
    </div>
  );
}
export default function App() {
  const [user, setUser] = useState(null);
  const [checking, setChecking] = useState(!!api.token);
  const [notice, setNotice] = useState(null);
  const [theme, setTheme] = useState(
    () =>
      localStorage.getItem("arguebot.theme") ||
      (matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark"),
  );
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem("arguebot.theme", theme);
  }, [theme]);
  useEffect(() => {
    if (api.token)
      api
        .me()
        .then(setUser)
        .catch(() => {
          api.token = "";
          sessionStorage.removeItem("arguebot.session");
        })
        .finally(() => setChecking(false));
  }, []);
  const { t } = useTranslation();
  const notify = useCallback((error) => {
    setNotice(error);
    if (error.status === 401) setUser(null);
  }, []);
  const toggleTheme = () =>
    setTheme((value) => (value === "dark" ? "light" : "dark"));
  if (checking)
    return <div className="initial-loading">{t("common.loading")}</div>;
  return (
    <BrowserRouter>
      <Suspense
        fallback={<div className="initial-loading">{t("common.loading")}</div>}
      >
        {user ? (
          <Routes>
            <Route
              element={
                <Shell
                  user={user}
                  theme={theme}
                  toggleTheme={toggleTheme}
                  notify={notify}
                  onLogout={async () => {
                    try {
                      await api.logout();
                    } finally {
                      setUser(null);
                    }
                  }}
                />
              }
            >
              <Route path="/dashboard/home" element={<Home />} />
              <Route
                path="/dashboard/conversations"
                element={<Conversations />}
              />
              <Route
                path="/dashboard/conversations/:id"
                element={<Workspace />}
              />
              <Route
                path="/dashboard/participants"
                element={<Participants />}
              />
              <Route path="/dashboard/history" element={<RequestHistory />} />
              <Route path="/dashboard/explorer" element={<ApiExplorer />} />
              <Route path="/dashboard/settings" element={<Settings />} />
              <Route path="/dashboard/backups" element={<Backups />} />
              <Route
                path="*"
                element={<Navigate to="/dashboard/home" replace />}
              />
            </Route>
          </Routes>
        ) : (
          <Login onLogin={setUser} theme={theme} toggleTheme={toggleTheme} />
        )}{" "}
        {notice && (
          <div className="toast" role="alert">
            <span>{notice.message}</span>
            <Button
              label="common.dismiss"
              help="common.dismiss"
              icon="close"
              iconOnly
              onClick={() => setNotice(null)}
            />
          </div>
        )}
      </Suspense>
    </BrowserRouter>
  );
}
