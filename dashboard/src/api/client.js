import i18n from "../i18n/index.js";
import transport from "../../../assets/transport.json";
export async function passwordDigest(password) {
  const bytes = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(password),
  );
  return Array.from(new Uint8Array(bytes), (byte) =>
    byte.toString(16).padStart(2, "0"),
  ).join("");
}
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
  async request(
    path,
    { method = "GET", body, signal, anonymous = false } = {},
  ) {
    const response = await fetch(path, {
      method,
      signal,
      headers: {
        ...(!anonymous && this.token
          ? { Authorization: `Bearer ${this.token}` }
          : {}),
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
      anonymous: true,
    });
    this.token = result.token;
    sessionStorage.setItem("arguebot.session", result.token);
    return this.me();
  }
  async login(tenantId, email, password) {
    if (location.protocol !== "https:")
      throw new ApiError(403, {
        code: "tls_required",
        message: i18n.t("login.tls.help"),
      });
    const result = await this.request("/v1.0/api/auth/login", {
      method: "POST",
      anonymous: true,
      body: { tenantId, email, passwordSha256: await passwordDigest(password) },
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
  note(id, text) {
    return this.request(this.root(id) + "/messages", {
      method: "POST",
      body: { action: "note", text, idempotencyKey: crypto.randomUUID() },
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
  backups(filter = {}) {
    return this.request("/v1.0/api/backups?" + new URLSearchParams(filter));
  }
  recoveryOperations(filter = {}) {
    return this.request(
      "/v1.0/api/backup-operations?" + new URLSearchParams(filter),
    );
  }
  recoveryOperation(id) {
    return this.request(
      "/v1.0/api/backup-operations/" + encodeURIComponent(id),
    );
  }
  recovery(kind, id, body) {
    return this.request(
      "/v1.0/api/backups" +
        (kind === "create" ? "" : "/" + encodeURIComponent(id) + "/" + kind),
      { method: "POST", body },
    );
  }
  deleteBackup(id) {
    return this.request("/v1.0/api/backups/" + encodeURIComponent(id), {
      method: "DELETE",
    });
  }
  async downloadBackup(id) {
    const response = await fetch(
      "/v1.0/api/backups/" + encodeURIComponent(id) + "/download",
      { headers: { Authorization: "Bearer " + this.token } },
    );
    if (!response.ok)
      throw new ApiError(
        response.status,
        await response.json().catch(() => ({})),
      );
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = url;
    link.download = id + ".arguebak";
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
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
          if (event === "session_revoked")
            throw new ApiError(401, {
              message: i18n.t("events.revoked"),
              code: "unauthenticated",
            });
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
