import brandIcon from "../../icon.png";
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
import { connectDashboard, hasDashboardConnection } from "./api/dashboard-connect.js";
import i18n, { registry } from "./i18n/index.js";
import { Button, ErrorPanel, Icon } from "./components/ui.jsx";
const Home = lazy(() => import("./views/Home.jsx"));
const Conversations = lazy(() => import("./views/Conversations.jsx"));
const Workspace = lazy(() => import("./views/Workspace.jsx"));
const Participants = lazy(() => import("./views/Participants.jsx"));
const RequestHistory = lazy(() => import("./views/RequestHistory.jsx"));
const ApiExplorer = lazy(() => import("./views/ApiExplorer.jsx"));
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
function Shell({ theme, toggleTheme, notify }) {
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
          <img src={brandIcon} alt="" width="36" height="36" />
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
            <span title={t("service.address.help")}>{location.host}</span>
            <span>{t("nav.localWorkspace")}</span>
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

          </div>
        </header>
        <div className="route-content">
          <Outlet context={{ api, notify }} />
        </div>
      </div>
    </div>
  );
}
export default function App() {
  const [connected, setConnected] = useState(false);
  const [checking, setChecking] = useState(hasDashboardConnection);
  const [connectionError, setConnectionError] = useState(null);
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
    connectDashboard()
      .then(() => setConnected(true))
      .catch((failure) => {
        api.token = "";
        sessionStorage.removeItem("arguebot.session");
        setConnectionError(failure);
      })
      .finally(() => setChecking(false));
  }, []);
  const { t } = useTranslation();
  const notify = useCallback((error) => {
    setNotice(error);
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
        {connected ? (
          <Routes>
            <Route
              element={
                <Shell
                  theme={theme}
                  toggleTheme={toggleTheme}
                  notify={notify}

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
              <Route
                path="*"
                element={<Navigate to="/dashboard/home" replace />}
              />
            </Route>
          </Routes>
        ) : (
          <main className="login"><section className="login-card">
            <h1>{t("service.unavailable")}</h1>
            <p>{t("service.unavailable.help")}</p>
            {connectionError && <ErrorPanel error={connectionError} />}
            <Button label="service.reconnect" onClick={() => location.reload()} />
          </section></main>
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
