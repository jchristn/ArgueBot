import i18n from "../i18n/index.js";
export class ApiError extends Error {
  constructor(status, data) {
    super(data.message || `HTTP ${status}`);
    this.status = status;
    this.code = data.code;
    this.requestId = data.requestId;
  }
}
export class ApiClient {
  constructor() {
    this.token = sessionStorage.getItem("arguebot.session") || "";
    this.clientId = crypto.randomUUID();
  }
  async request(path, { method = "GET", body, signal } = {}) {
    const response = await fetch(path, {
      method,
      signal,
      headers: {
        ...(this.token ? { Authorization: `Bearer ${this.token}` } : {}),
        "Content-Type": "application/json",
        "Accept-Language": i18n.language,
        "x-client-id": this.clientId,
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new ApiError(response.status, data);
    return data;
  }
  async pair(code) {
    const result = await this.request("/v1.0/api/auth/pair", {
      method: "POST",
      body: { code },
    });
    this.token = result.token;
    sessionStorage.setItem("arguebot.session", result.token);
    return this.me();
  }
  me() {
    return this.request("/v1.0/api/auth/me");
  }
  async logout() {
    try {
      await this.request("/v1.0/api/auth/logout", { method: "POST", body: {} });
    } finally {
      this.token = "";
      sessionStorage.removeItem("arguebot.session");
    }
  }
  conversations({
    search = "",
    archived = false,
    pageNumber = 1,
    pageSize = 25,
  } = {}) {
    return this.request(
      "/v1.0/api/conversations?" +
        new URLSearchParams({ search, archived, pageNumber, pageSize }),
    );
  }
  createConversation(title) {
    return this.request("/v1.0/api/conversations", {
      method: "POST",
      body: { title },
    });
  }
  conversation(id) {
    return this.request(this.root(id));
  }
  updateConversation(record) {
    return this.request(this.root(record.id), { method: "PUT", body: record });
  }
  deleteConversation(id) {
    return this.request(this.root(id), { method: "DELETE" });
  }
  messages(id) {
    return this.request(this.root(id) + "/messages");
  }
  note(id, text) {
    return this.request(this.root(id) + "/messages", {
      method: "POST",
      body: { action: "note", text, idempotencyKey: crypto.randomUUID() },
    });
  }
  runs(id) {
    return this.request(this.root(id) + "/runs");
  }
  startRun(id, request) {
    return this.request(this.root(id) + "/runs", {
      method: "POST",
      body: { ...request, idempotencyKey: crypto.randomUUID() },
    });
  }
  action(id, runId, request) {
    return this.request(
      this.root(id) + "/runs/" + encodeURIComponent(runId) + "/actions",
      {
        method: "POST",
        body: { ...request, idempotencyKey: crypto.randomUUID() },
      },
    );
  }
  participants() {
    return this.request("/v1.0/api/participants");
  }
  createParticipant(body) {
    return this.request("/v1.0/api/participants", { method: "POST", body });
  }
  updateParticipant(profile) {
    return this.request(
      "/v1.0/api/participants/" + encodeURIComponent(profile.id),
      { method: "PUT", body: profile },
    );
  }
  deleteParticipant(id) {
    return this.request("/v1.0/api/participants/" + encodeURIComponent(id), {
      method: "DELETE",
    });
  }
  saveSettings(body) {
    return this.request("/v1.0/api/settings", { method: "PUT", body });
  }
  restart() {
    return this.request("/v1.0/api/settings/restart", {
      method: "POST",
      body: {},
    });
  }
  history(filter = {}) {
    return this.request(
      "/v1.0/api/request-history?" +
        new URLSearchParams(
          Object.entries(filter).filter(
            ([, value]) => value !== "" && value != null,
          ),
        ),
    );
  }
  historyDetail(id) {
    return this.request("/v1.0/api/request-history/" + encodeURIComponent(id));
  }
  deleteHistory(id) {
    return this.request("/v1.0/api/request-history/" + encodeURIComponent(id), {
      method: "DELETE",
    });
  }
  historySummary(fromUtc, toUtc, bucketMinutes = 60) {
    return this.request(
      "/v1.0/api/request-history/summary?" +
        new URLSearchParams({ fromUtc, toUtc, bucketMinutes }),
    );
  }
  openApi() {
    return this.request("/openapi.json");
  }
  async executeExplorer(path, method, body, headers = {}) {
    if (!path.startsWith("/v1.0/") && path !== "/health")
      throw new ApiError(400, {
        code: "invalid_path",
        message: i18n.t("explorer.invalidPath"),
      });
    const started = performance.now();
    const response = await fetch(path, {
      method,
      headers: {
        Authorization: `Bearer ${this.token}`,
        "Content-Type": "application/json",
        ...headers,
      },
      ...(method !== "GET" && method !== "HEAD" && body ? { body } : {}),
    });
    return {
      status: response.status,
      headers: Array.from(response.headers.entries()),
      body: await response.text(),
      durationMs: Math.round(performance.now() - started),
    };
  }
  settings() {
    return this.request("/v1.0/api/settings");
  }
  root(id) {
    return "/v1.0/api/conversations/" + encodeURIComponent(id);
  }
  async events(id, cursor, onEvent, signal) {
    const response = await fetch(this.root(id) + "/events", {
      signal,
      headers: {
        Authorization: `Bearer ${this.token}`,
        Accept: "text/event-stream",
        ...(cursor ? { "Last-Event-ID": cursor } : {}),
      },
    });
    if (!response.ok)
      throw new ApiError(
        response.status,
        await response.json().catch(() => ({})),
      );
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = "";
    try {
      while (!signal.aborted) {
        const { done, value } = await reader.read();
        if (done) break;
        pending += decoder
          .decode(value, { stream: true })
          .replace(/\r\n/g, "\n");
        let boundary;
        while ((boundary = pending.indexOf("\n\n")) >= 0) {
          const frame = pending.slice(0, boundary);
          pending = pending.slice(boundary + 2);
          const fields = frame.split("\n");
          const event = fields
            .find((line) => line.startsWith("event:"))
            ?.slice(6)
            .trim();
          if (event === "session_revoked")
            throw new ApiError(401, {
              message: "Session revoked",
              code: "unauthenticated",
            });
          if (event !== "change") continue;
          const eventId = fields
            .find((line) => line.startsWith("id:"))
            ?.slice(3)
            .trim();
          const data = fields
            .filter((line) => line.startsWith("data:"))
            .map((line) => line.slice(5).trimStart())
            .join("\n");
          onEvent(JSON.parse(data), eventId);
        }
      }
    } finally {
      reader.releaseLock();
    }
  }
}
export const api = new ApiClient();
