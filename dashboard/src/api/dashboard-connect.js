import { api } from "./client.js";

// Remove obsolete connection fragments from earlier builds without using them.
if (window.location.hash.startsWith("#connect="))
  window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);

export const hasDashboardConnection = true;
let connection;
export function connectDashboard() {
  api.token = "";
  sessionStorage.removeItem("arguebot.session");
  connection ??= api.request("/health");
  return connection;
}
