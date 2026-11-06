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
  useLocation,
  useNavigate,
  useOutletContext,
} from "react-router-dom";
import { useTranslation } from "react-i18next";
import { api } from "./api/client.js";
import { connectDashboard, hasDashboardConnection } from "./api/dashboard-connect.js";
import { expireSession, signOut, subscribe } from "./api/session.js";
import ActionMenu from "./components/ActionMenu.jsx";
import { Language, ThemeControl } from "./components/Chrome.jsx";
import { Button, ErrorPanel, Icon } from "./components/ui.jsx";
import Toast from "./components/Toast.jsx";
import Login, { returnPath } from "./views/Login.jsx";
const Home = lazy(() => import("./views/Home.jsx"));
const Conversations = lazy(() => import("./views/Conversations.jsx"));
const Workspace = lazy(() => import("./views/Workspace.jsx"));
const Participants = lazy(() => import("./views/Participants.jsx"));
const Prompts = lazy(() => import("./views/Prompts.jsx"));
const TranscriptReader = lazy(() => import("./views/TranscriptReader.jsx"));
const RequestHistory = lazy(() => import("./views/RequestHistory.jsx"));
const ApiExplorer = lazy(() => import("./views/ApiExplorer.jsx"));
const Settings = lazy(() => import("./views/Settings.jsx"));
const Account = lazy(() => import("./views/Account.jsx"));
const Users = lazy(() => import("./views/Users.jsx"));
const Tenants = lazy(() => import("./views/Tenants.jsx"));
const Audit = lazy(() => import("./views/Audit.jsx"));
/** A user's display name: first and last name, or the email. */
export function displayName(user) {
  return [user?.firstName, user?.lastName].filter(Boolean).join(" ") || user?.email || "";
}
/** Navigation for the signed-in user. Administration appears only for administrators. */
export function navigationGroups(user) {
  const tenantAdmin = !!(user?.isAdmin || user?.isTenantAdmin);
  return [
    { title: "nav.operate", links: [["home", "nav.home", "grid"], ["conversations", "nav.conversations", "chat"]] },
    { title: "nav.configure", links: [["participants", "nav.participants", "users"], ["prompts", "nav.prompts", "prompt"]] },
    { title: "nav.system", links: [...(tenantAdmin ? [["history", "history.title", "chat"]] : []), ["explorer", "explorer.title", "grid"], ...(user?.isAdmin ? [["settings", "nav.settings", "settings"]] : [])] },
    ...(tenantAdmin ? [{ title: "nav.administration", links: [["users", "nav.users", "users"], ...(user?.isAdmin ? [["tenants", "nav.tenants", "grid"]] : []), ["audit", "nav.audit", "prompt"]] }] : []),
  ];
}
/** Routes that need a signed-in user send everyone else to the sign-in page, remembering where they were going. */
function RequireSession({ session, expired }) {
  const location = useLocation();
  if (!session) return <Navigate to="/dashboard/login" replace state={{ from: location.pathname + location.search, expired }} />;
  return <Outlet />;
}
/** Only administrators reach administration pages; others go home. */
function RequireRole({ session, role }) {
  const context = useOutletContext();
  const allowed = role === "system" ? session?.user?.isAdmin : session?.user?.isAdmin || session?.user?.isTenantAdmin;
  return allowed ? <Outlet context={context} /> : <Navigate to="/dashboard/home" replace />;
}
function AccountMenu({ session }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const name = displayName(session.user);
  return <ActionMenu label="account.menu" triggerClassName="account-trigger" items={[
    { label: "account.title", onClick: () => navigate("/dashboard/account") },
    { label: "account.signOut", onClick: async () => { await signOut(); navigate("/dashboard/login", { replace: true }); } },
  ]}>
    <Icon name="users" /><span className="account-name">{name}</span><span className="badge tenant-badge" title={t("account.tenant.help")}>{session.tenant?.name}</span>
  </ActionMenu>;
}
function Shell({ theme, toggleTheme, notify, session }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const groups = navigationGroups(session.user);
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
          <code>2.2.0</code>
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
            <span className="badge address-badge" title={t("service.address.help")}><code>{location.host}</code></span>
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
              <GitHubMark />
            </a>
            <ThemeControl theme={theme} toggle={toggleTheme} />
            <Language />
            <AccountMenu session={session} />
          </div>
        </header>
        <main className="route-content">
          <Outlet context={{ api, notify, session }} />
        </main>
      </div>
    </div>
  );
}
/** GitHub's mark (from GitHub's Octicons, MIT license), filled so it reads at icon size. */
function GitHubMark() {
  return <svg viewBox="0 0 16 16" width="20" height="20" fill="currentColor" aria-hidden="true"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z" /></svg>;
}
export default function App() {
  const [connected, setConnected] = useState(false);
  const [checking, setChecking] = useState(hasDashboardConnection);
  const [connectionError, setConnectionError] = useState(null);
  const [session, setSession] = useState(null);
  const [expired, setExpired] = useState(false);
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
    const unsubscribe = subscribe((value) => setSession(value));
    // A rejected token anywhere signs the dashboard out and returns to the sign-in page.
    api.onUnauthorized = () => { setExpired(true); expireSession(); };
    connectDashboard()
      .then((restored) => { setSession(restored); setConnected(true); })
      .catch((failure) => setConnectionError(failure))
      .finally(() => setChecking(false));
    return () => { unsubscribe(); api.onUnauthorized = null; };
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
            <Route path="/share/:token" element={<TranscriptReader />} />
            <Route path="/dashboard/login" element={session ? <SignedIn /> : <Login theme={theme} toggleTheme={toggleTheme} onSignedIn={(value) => { setExpired(false); setSession(value); }} />} />
            <Route element={<RequireSession session={session} expired={expired} />}>
              <Route path="/dashboard/conversations/:id/read" element={<TranscriptReader />} />
              <Route element={<Shell theme={theme} toggleTheme={toggleTheme} notify={notify} session={session} />}>
                <Route path="/dashboard/home" element={<Home />} />
                <Route path="/dashboard/conversations" element={<Conversations />} />
                <Route path="/dashboard/conversations/:id" element={<Workspace />} />
                <Route path="/dashboard/participants" element={<Participants />} />
                <Route path="/dashboard/prompts" element={<Prompts />} />
                <Route path="/dashboard/explorer" element={<ApiExplorer />} />
                <Route path="/dashboard/account" element={<Account />} />
                <Route element={<RequireRole session={session} role="tenant" />}>
                  <Route path="/dashboard/history" element={<RequestHistory />} />
                  <Route path="/dashboard/users" element={<Users />} />
                  <Route path="/dashboard/audit" element={<Audit />} />
                </Route>
                <Route element={<RequireRole session={session} role="system" />}>
                  <Route path="/dashboard/settings" element={<Settings />} />
                  <Route path="/dashboard/tenants" element={<Tenants />} />
                </Route>
                <Route path="*" element={<Navigate to="/dashboard/home" replace />} />
              </Route>
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
        {notice && <Toast message={notice.message} onDismiss={() => setNotice(null)} />}
      </Suspense>
    </BrowserRouter>
  );
}
/** Visiting the sign-in page while signed in continues to the requested page. */
function SignedIn() {
  const location = useLocation();
  return <Navigate to={returnPath(location)} replace />;
}
