import { api } from "./client.js";

/**
 * The signed-in session. The token lives in sessionStorage only: it is cleared when the browser
 * closes, never written to localStorage or cookies, and never placed in a URL.
 */
const key = "arguebot.session";
const listeners = new Set();
let current = null;
let refreshTimer = null;

function publish() { for (const listener of listeners) listener(current); }
function store(value) {
  current = value;
  api.token = value?.token || "";
  if (value) sessionStorage.setItem(key, JSON.stringify(value));
  else sessionStorage.removeItem(key);
  schedule();
  publish();
}
/** Refresh a session token five minutes before it expires; credential tokens do not expire this way. */
function schedule() {
  clearTimeout(refreshTimer);
  refreshTimer = null;
  if (!current?.token?.startsWith("abs_") || !current.expiresUtc) return;
  const wait = Math.max(5000, Date.parse(current.expiresUtc) - Date.now() - 5 * 60 * 1000);
  refreshTimer = setTimeout(async () => {
    try {
      const response = await api.refreshToken();
      store({ ...current, token: response.token, expiresUtc: response.expiresUtc, user: response.user, tenant: response.tenant });
    } catch {
      /* A failed refresh leaves the current token; the next 401 returns to the sign-in page. */
    }
  }, wait);
}

export function session() { return current; }
export function subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); }

/** Restore a stored session and confirm it is still valid. Resolves to the session or null. */
export async function restoreSession() {
  let saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(key) || "null"); } catch { saved = null; }
  if (!saved?.token) { store(null); return null; }
  api.token = saved.token;
  try {
    const details = await api.tokenDetails();
    store({ ...saved, user: details.user, tenant: details.tenant, principalType: details.principalType, expiresUtc: details.expiresUtc || saved.expiresUtc || null });
    return current;
  } catch {
    store(null);
    return null;
  }
}

/** Sign in with an email and password. Throws ApiError, including 409 tenant_required with a tenants list. */
export async function signIn(email, password, tenantId = "") {
  const response = await api.login(email, password, tenantId);
  store({ token: response.token, expiresUtc: response.expiresUtc, user: response.user, tenant: response.tenant, principalType: "User" });
  return current;
}

/** Sign in with a bearer token (a credential token or an existing session token). */
export async function signInWithToken(token) {
  api.token = token.trim();
  try {
    const details = await api.tokenDetails();
    store({ token: api.token, expiresUtc: details.expiresUtc || null, user: details.user, tenant: details.tenant, principalType: details.principalType });
    return current;
  } catch (error) {
    api.token = current?.token || "";
    throw error;
  }
}

/** Update the stored user after an account change. */
export function updateSessionUser(user) { if (current) store({ ...current, user }); }

/** Sign out: revoke a session token on the server, then forget it locally. */
export async function signOut() {
  if (current?.token?.startsWith("abs_")) {
    try { await api.logout(); } catch { /* The session may already be gone. */ }
  }
  store(null);
}

/** Forget the session without calling the server, after it was rejected. */
export function expireSession() { store(null); }
