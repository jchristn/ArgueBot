import { api } from "./client.js";
import { restoreSession } from "./session.js";

// Remove obsolete connection fragments from earlier builds without using them.
if (window.location.hash.startsWith("#connect="))
  window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);

export const hasDashboardConnection = true;
let connection;
/** Check that the service answers, then restore a stored session if it is still valid. Resolves to the session or null. */
export function connectDashboard() {
  connection ??= api.request("/health").then(() => restoreSession());
  return connection;
}
