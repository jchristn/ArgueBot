import { randomId } from "../utils/random-id.js";
import i18n from "../i18n/index.js";
import transport from "../../../assets/transport.json";
export class ApiError extends Error {
  constructor(status, data) {
    super(data.message || `HTTP ${status}`);
    this.status = status;
    this.code = data.code;
    this.field = data.field || "";
    this.requestId = data.requestId;
    this.tenants = Array.isArray(data.tenants) ? data.tenants : [];
    this.requiredPermission = data.requiredPermission || "";
  }
}
/** Configuration resource names used by the views, mapped to their 2.1 routes. */
const configurationRoute = (resource) => (resource === "credentials" ? "secrets" : resource);
export class ApiClient {
  constructor() {
    this.token = "";
    this.clientId = randomId();
    /** Called with the ApiError when a request returns 401, so the app can sign out and show the sign-in page. */
    this.onUnauthorized = null;
  }
  authorization() {
    return this.token ? { Authorization: "Bearer " + this.token } : {};
  }
  failed(status, data) {
    const error = new ApiError(status, data);
    if (status === 401 && this.token && this.onUnauthorized) this.onUnauthorized(error);
    return error;
  }
  async request(
    path,
    { method = "GET", body, signal, headers = {} } = {},
  ) {
    const response = await fetch(path, {
      method,
      signal,
      headers: {
        "Content-Type": "application/json",
        "Accept-Language": i18n.language,
        "x-client-id": this.clientId,
        ...this.authorization(),
        ...headers,
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw this.failed(response.status, data);
    return data;
  }
  loginInfo() { return this.request("/v2.0/api/login-info"); }
  login(email, password, tenantId = "") { return this.request("/v2.0/api/token", { method: "POST", body: { email, password, tenantId } }); }
  refreshToken() { return this.request("/v2.0/api/token/refresh", { method: "POST" }); }
  logout() { return this.request("/v2.0/api/token", { method: "DELETE" }); }
  tokenDetails() { return this.request("/v2.0/api/token/details"); }
  me() { return this.request("/v2.0/api/me"); }
  updateMe(body) { return this.request("/v2.0/api/me", { method: "PUT", body }); }
  changePassword(currentPassword, newPassword) { return this.request("/v2.0/api/me/password", { method: "PUT", body: { currentPassword, newPassword } }); }
  /** x-tenant-id for a system administrator acting in another tenant; empty for the caller's own tenant. */
  tenantScope(tenantId) { return tenantId ? { "x-tenant-id": tenantId } : {}; }
  userRoot(userId) { return userId ? "/v2.0/api/users/" + encodeURIComponent(userId) : "/v2.0/api/me"; }
  credentials(userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/credentials", { headers: this.tenantScope(tenantId) }); }
  createCredential(body, userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/credentials", { method: "POST", body, headers: this.tenantScope(tenantId) }); }
  updateCredential(id, body, userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/credentials/" + encodeURIComponent(id), { method: "PUT", body, headers: this.tenantScope(tenantId) }); }
  revealToken(id, userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/credentials/" + encodeURIComponent(id) + "/reveal", { method: "POST", headers: this.tenantScope(tenantId) }); }
  deleteCredential(id, userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/credentials/" + encodeURIComponent(id), { method: "DELETE", headers: this.tenantScope(tenantId) }); }
  sessions(userId = "", tenantId = "") { return this.request(this.userRoot(userId) + "/sessions", { headers: this.tenantScope(tenantId) }); }
  revokeSession(id, own = true, tenantId = "") { return this.request((own ? "/v2.0/api/me/sessions/" : "/v2.0/api/sessions/") + encodeURIComponent(id), { method: "DELETE", headers: this.tenantScope(tenantId) }); }
  users(tenantId = "") { return this.request("/v2.0/api/users", { headers: this.tenantScope(tenantId) }); }
  saveUser(body, tenantId = "") { const { id, ...value } = body; return this.request("/v2.0/api/users" + (id ? "/" + encodeURIComponent(id) : ""), { method: id ? "PUT" : "POST", body: value, headers: this.tenantScope(tenantId) }); }
  deleteUser(id, tenantId = "") { return this.request("/v2.0/api/users/" + encodeURIComponent(id), { method: "DELETE", headers: this.tenantScope(tenantId) }); }
  resetPassword(id, newPassword, tenantId = "") { return this.request("/v2.0/api/users/" + encodeURIComponent(id) + "/password", { method: "PUT", body: { newPassword }, headers: this.tenantScope(tenantId) }); }
  tenants() { return this.request("/v2.0/api/tenants"); }
  saveTenant(body) { const { id, ...value } = body; return this.request("/v2.0/api/tenants" + (id ? "/" + encodeURIComponent(id) : ""), { method: id ? "PUT" : "POST", body: value }); }
  deleteTenant(id, confirmName) { return this.request("/v2.0/api/tenants/" + encodeURIComponent(id) + "?confirmName=" + encodeURIComponent(confirmName), { method: "DELETE" }); }
  audit(filters = {}) {
    const query = new URLSearchParams(Object.entries({ pageNumber: 1, pageSize: 50, ...filters }).filter(([, value]) => value !== "" && value !== undefined && value !== null));
    return this.request("/v2.0/api/audit?" + query);
  }
  deleteAudit(beforeUtc) { return this.request("/v2.0/api/audit?beforeUtc=" + encodeURIComponent(beforeUtc), { method: "DELETE" }); }

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
  createConversation(title, openingQuestion = "", promptIds = {}) {
    return this.request("/v1.0/api/conversations", {
      method: "POST",
      body: { title, openingQuestion, promptIds },
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
  messages(
    id,
    { pageNumber = 1, pageSize = 25, throughSequence, tail = false } = {},
  ) {
    const query = new URLSearchParams({ pageNumber, pageSize, tail });
    if (throughSequence !== undefined)
      query.set("throughSequence", throughSequence);
    return this.request(this.root(id) + "/messages?" + query);
  }
  message(id, messageId) {
    return this.request(
      this.root(id) + "/messages/" + encodeURIComponent(messageId),
    );
  }
  async allMessages(id) {
    let page = await this.messages(id, { pageSize: 100 });
    const throughSequence = page.throughSequence;
    const expected = page.totalCount;
    const result = [];
    let sequence = 0;
    while (result.length < expected) {
      if (
        page.throughSequence !== throughSequence ||
        page.totalCount !== expected ||
        page.items.length === 0
      )
        throw new ApiError(409, {
          code: "history_changed",
          message: i18n.t("messages.changed"),
        });
      for (const preview of page.items) {
        if (
          preview.sequence <= sequence ||
          preview.sequence > throughSequence ||
          result.length >= expected
        )
          throw new ApiError(409, {
            code: "history_changed",
            message: i18n.t("messages.changed"),
          });
        const message = preview.contentTruncated
          ? await this.message(id, preview.id)
          : preview;
        if (
          message.contentTruncated ||
          message.id !== preview.id ||
          message.sequence !== preview.sequence
        )
          throw new ApiError(409, {
            code: "history_changed",
            message: i18n.t("messages.changed"),
          });
        result.push(message);
        sequence = message.sequence;
      }
      if (result.length < expected)
        page = await this.messages(id, {
          pageNumber: page.pageNumber + 1,
          pageSize: 100,
          throughSequence,
        });
    }
    return result;
  }
  modelChecks(id, page = 1) { return this.request(`/v2.0/api/configuration/models/${encodeURIComponent(id)}/checks?pageNumber=${page}`); }
  checkModel(id, body) { return this.request(`/v2.0/api/configuration/models/${encodeURIComponent(id)}/checks`, { method: "POST", body }); }
  agentChecks(id, page = 1) { return this.request(`/v2.0/api/participants/${encodeURIComponent(id)}/checks?pageNumber=${page}`); }
  checkAgent(id, body) { return this.request(`/v2.0/api/participants/${encodeURIComponent(id)}/checks`, { method: "POST", body }); }
  credentialKeys() { return this.request("/v2.0/api/configuration/secret-keys"); }
  rotateCredentialKey(expectedKeyId) { return this.request("/v2.0/api/configuration/secret-keys/rotate", { method: "POST", body: { expectedKeyId } }); }
  retireCredentialKeys(expectedKeyId) { return this.request("/v2.0/api/configuration/secret-keys/retire", { method: "POST", body: { expectedKeyId } }); }
  summaries(id, runId, pageNumber = 1) { return this.request(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/summaries?pageNumber=" + pageNumber); }
  summary(id, runId, summaryId, sourcePageNumber = 1) { return this.request(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/summaries?id=" + encodeURIComponent(summaryId) + "&sourcePageNumber=" + sourcePageNumber); }
  clarifications(id, runId) { return this.readListPages(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/clarifications", 100); }
  previewEndpoint(id) { return this.request("/v2.0/api/configuration/endpoints/" + encodeURIComponent(id) + "/preview"); }
  discoverEndpoint(id) { return this.request("/v2.0/api/configuration/endpoints/" + encodeURIComponent(id) + "/discover", { method: "POST" }); }
  async readListPages(route, size) {
    const result = [];
    for (let page = 1; ; page++) {
      const items = await this.request(route + "?pageNumber=" + page);
      result.push(...items);
      if (items.length < size) return result;
    }
  }
  async configuration(resource) {
    const result = [];
    for (let page = 1; ; page++) {
      const items = await this.request("/v2.0/api/configuration/" + configurationRoute(resource) + "?pageSize=100&pageNumber=" + page);
      result.push(...items);
      if (items.length < 100) return result;
    }
  }
  saveConfiguration(resource, body) { const { id, ...value } = body; return this.request("/v2.0/api/configuration/" + configurationRoute(resource) + (id ? "/" + encodeURIComponent(id) : ""), { method: id ? "PUT" : "POST", body: value }); }
  prompts(purpose = "") { return this.request("/v2.0/api/prompts" + (purpose ? "?purpose=" + encodeURIComponent(purpose) : "")); }
  prompt(id) { return this.request("/v2.0/api/prompts/" + encodeURIComponent(id)); }
  savePrompt(prompt) { return this.request("/v2.0/api/prompts" + (prompt.id ? "/" + encodeURIComponent(prompt.id) : ""), { method: prompt.id ? "PUT" : "POST", body: prompt }); }
  duplicatePrompt(id) { return this.request("/v2.0/api/prompts/" + encodeURIComponent(id) + "/duplicate", { method: "POST" }); }
  deletePrompt(id, revision) { return this.request("/v2.0/api/prompts/" + encodeURIComponent(id) + "?revision=" + revision, { method: "DELETE" }); }
  promptParameters() { return this.request("/v2.0/api/prompt-parameters"); }
  resources({ search = "", tag = "", pageNumber = 1, pageSize = 200 } = {}) {
    const query = new URLSearchParams({ pageNumber: String(pageNumber), pageSize: String(pageSize) });
    if (search) query.set("search", search);
    if (tag) query.set("tag", tag);
    return this.request("/v2.0/api/resources?" + query);
  }
  resource(id) { return this.request("/v2.0/api/resources/" + encodeURIComponent(id)); }
  resourceByName(name) { return this.request("/v2.0/api/resources/by-name/" + name.split("/").map(encodeURIComponent).join("/")); }
  saveResource(resource) {
    return this.request("/v2.0/api/resources" + (resource.id ? "/" + encodeURIComponent(resource.id) : ""), { method: resource.id ? "PUT" : "POST", body: resource });
  }
  deleteResource(id, revision) { return this.request("/v2.0/api/resources/" + encodeURIComponent(id) + "?revision=" + revision, { method: "DELETE" }); }
  resourceRevisions(id) { return this.request("/v2.0/api/resources/" + encodeURIComponent(id) + "/revisions"); }
  resourceRevision(id, revision) { return this.request("/v2.0/api/resources/" + encodeURIComponent(id) + "/revisions/" + revision); }
  revertResource(id, targetRevision, revision) { return this.request("/v2.0/api/resources/" + encodeURIComponent(id) + "/revert", { method: "POST", body: { targetRevision, revision } }); }
  importResources(files, overwrite = false) { return this.request("/v2.0/api/resources/import", { method: "POST", body: { files, overwrite } }); }
  async exportResource(id) {
    const response = await fetch("/v2.0/api/resources/" + encodeURIComponent(id) + "/export", { headers: { "Accept-Language": i18n.language, "x-client-id": this.clientId, ...this.authorization() } });
    if (!response.ok) throw this.failed(response.status, await response.json().catch(() => ({})));
    return response.text();
  }
  revealCredential(id) { return this.request("/v2.0/api/configuration/secrets/" + encodeURIComponent(id) + "/reveal", { method: "POST" }); }
  disableCredential(id, revision) { return this.request("/v2.0/api/configuration/secrets/" + encodeURIComponent(id) + "?revision=" + revision, { method: "DELETE" }); }
  capabilities() { return this.request("/v2.0/api/capabilities"); }
  shares(conversationId) { return this.request(this.v2root(conversationId) + "/shares"); }
  createShare(conversationId, body) { return this.request(this.v2root(conversationId) + "/shares", { method: "POST", body }); }
  revealShare(conversationId, shareId) { return this.request(this.v2root(conversationId) + "/shares/" + encodeURIComponent(shareId) + "/reveal", { method: "POST" }); }
  revokeShare(conversationId, shareId) { return this.request(this.v2root(conversationId) + "/shares/" + encodeURIComponent(shareId), { method: "DELETE" }); }
  sharedTranscript(shareToken) { return this.request("/v2.0/api/shares/" + encodeURIComponent(shareToken)); }
  v2root(id) { return "/v2.0/api/conversations/" + encodeURIComponent(id); }
  interject(id, body) { return this.request(this.v2root(id) + "/interjections", { method: "POST", body }); }
  interjections(id, pageNumber = 1) { return this.request(this.v2root(id) + "/interjections?pageNumber=" + pageNumber); }
  proposals(id, runId) { return this.readListPages(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/proposals", 50); }
  roster(id, runId) { return this.request(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/roster"); }
  changeRoster(id, runId, body) { return this.request(this.v2root(id) + "/runs/" + encodeURIComponent(runId) + "/roster", { method: "POST", body }); }
  note(id, text) {
    return this.request(this.root(id) + "/messages", {
      method: "POST",
      body: { action: "note", text, idempotencyKey: randomId() },
    });
  }
  runs(id) {
    return this.request(this.root(id) + "/runs");
  }
  attempts(id, runId, pageNumber = 1, pageSize = 25) {
    return this.request(
      this.root(id) +
        "/runs/" +
        encodeURIComponent(runId) +
        "/attempts?" +
        new URLSearchParams({ pageNumber, pageSize }),
    );
  }
  startRun(id, request) {
    return this.request((request.protocolVersion === 2 ? this.v2root(id) : this.root(id)) + "/runs", {
      method: "POST",
      body: { ...request, idempotencyKey: request.idempotencyKey || randomId() },
    });
  }
  action(id, runId, request) {
    return this.request(
      this.root(id) + "/runs/" + encodeURIComponent(runId) + "/actions",
      {
        method: "POST",
        body: { ...request, idempotencyKey: request.idempotencyKey || randomId() },
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
    if (!path.startsWith("/v1.0/") && !path.startsWith("/v2.0/") && path !== "/health")
      throw new ApiError(400, {
        code: "invalid_path",
        message: i18n.t("explorer.invalidPath"),
      });
    const started = performance.now();
    const response = await fetch(path, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...this.authorization(),
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
        Accept: "text/event-stream",
        ...this.authorization(),
        ...(cursor ? { "Last-Event-ID": cursor } : {}),
      },
    });
    if (!response.ok)
      throw this.failed(
        response.status,
        await response.json().catch(() => ({})),
      );
    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8", { fatal: true });
    let pending = "";
    let carriageReturn = false;
    let last = cursor.startsWith(id + ":")
      ? Number(cursor.slice(id.length + 1))
      : 0;
    const invalid = (code = "invalid_event", status = 502) =>
      new ApiError(status, { code, message: i18n.t("events.refresh") });
    try {
      while (!signal.aborted) {
        const { done, value } = await reader.read();
        if (done) {
          try {
            decoder.decode();
          } catch {
            throw invalid();
          }
          break;
        }
        let decoded;
        try {
          decoded = decoder.decode(value, { stream: true });
        } catch {
          throw invalid();
        }
        if (decoded.length) {
          const omitLineFeed = carriageReturn && decoded.startsWith("\n");
          carriageReturn = decoded.endsWith("\r");
          pending += (omitLineFeed ? decoded.slice(1) : decoded).replace(
            /\r\n|\r/g,
            "\n",
          );
        }
        let boundary;
        while ((boundary = pending.indexOf("\n\n")) >= 0) {
          if (boundary > transport.maxEventFrameCharacters)
            throw invalid("event_frame_too_large");
          const frame = pending.slice(0, boundary);
          pending = pending.slice(boundary + 2);
          const fields = frame.split("\n");
          const event = fields
            .find((line) => line.startsWith("event:"))
            ?.slice(6)
            .trim();
          if (event !== "change" && event !== "error") continue;
          const raw = fields
            .filter((line) => line.startsWith("data:"))
            .map((line) => line.slice(5).trimStart())
            .join("\n");
          let data;
          try {
            data = JSON.parse(raw);
          } catch {
            throw invalid();
          }
          if (event === "error") {
            if (!data || typeof data.code !== "string") throw invalid();
            throw new ApiError(
              data.code === "event_history_gap" ||
              data.code === "invalid_cursor"
                ? 409
                : 503,
              data,
            );
          }
          const eventId = fields
            .find((line) => line.startsWith("id:"))
            ?.slice(3)
            .trim();
          if (
            !data ||
            data.conversationId !== id ||
            !Number.isSafeInteger(data.sequence) ||
            data.sequence < 1 ||
            eventId !== id + ":" + data.sequence
          )
            throw invalid();
          if (data.sequence <= last) continue;
          if (last > 0 && data.sequence !== last + 1)
            throw invalid("event_history_gap", 409);
          last = data.sequence;
          onEvent(data, eventId);
        }
        if (pending.length > transport.maxEventFrameCharacters)
          throw invalid("event_frame_too_large");
      }
    } finally {
      try {
        await reader.cancel();
      } catch {
        /* The transport can already be aborted. */
      }
      reader.releaseLock();
    }
  }
}
export const api = new ApiClient();
