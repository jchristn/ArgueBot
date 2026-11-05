import { afterEach, beforeEach, expect, it, vi } from "vitest";

let fetchMock;
beforeEach(() => { vi.resetModules(); sessionStorage.clear(); fetchMock = vi.fn(); vi.stubGlobal("fetch", fetchMock); });
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
const reply = (status, body) => Promise.resolve({ ok: status < 400, status, json: () => Promise.resolve(body) });
const user = { id: "usr_local", email: "admin@arguebot", isAdmin: true, isTenantAdmin: true };
const tenant = { id: "ten_local", name: "Default tenant" };

it("signs in, keeps the token only in sessionStorage and sends it as a bearer token", async () => {
  const { api } = await import("./client.js");
  const { signIn, session } = await import("./session.js");
  fetchMock.mockReturnValueOnce(reply(201, { token: "abs_session", expiresUtc: new Date(Date.now() + 3600000).toISOString(), user, tenant }));
  await signIn("admin@arguebot", "password");
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toMatchObject({ email: "admin@arguebot", password: "password", tenantId: "" });
  expect(session().user.email).toBe("admin@arguebot");
  expect(JSON.parse(sessionStorage.getItem("arguebot.session")).token).toBe("abs_session");
  expect(localStorage.getItem("arguebot.session")).toBeNull();
  fetchMock.mockReturnValueOnce(reply(200, { items: [] }));
  await api.conversations();
  expect(fetchMock.mock.calls[1][1].headers.Authorization).toBe("Bearer abs_session");
});

it("restores a stored session only after the service accepts it", async () => {
  sessionStorage.setItem("arguebot.session", JSON.stringify({ token: "abs_stale" }));
  const { restoreSession } = await import("./session.js");
  fetchMock.mockReturnValueOnce(reply(401, { code: "session_expired", message: "Your session expired." }));
  await expect(restoreSession()).resolves.toBeNull();
  expect(sessionStorage.getItem("arguebot.session")).toBeNull();
  sessionStorage.setItem("arguebot.session", JSON.stringify({ token: "abt_valid" }));
  fetchMock.mockReturnValueOnce(reply(200, { principalType: "Credential", user, tenant, expiresUtc: null }));
  const restored = await restoreSession();
  expect(restored).toMatchObject({ token: "abt_valid", principalType: "Credential", tenant: { name: "Default tenant" } });
});

it("reports a tenant choice and a rejected token to the app", async () => {
  const { api } = await import("./client.js");
  const { signIn, signInWithToken, session } = await import("./session.js");
  fetchMock.mockReturnValueOnce(reply(409, { code: "tenant_required", message: "Choose a tenant.", tenants: [{ id: "ten_a", name: "A" }, { id: "ten_b", name: "B" }] }));
  await expect(signIn("shared@example.test", "secret-password")).rejects.toMatchObject({ code: "tenant_required", tenants: [{ id: "ten_a" }, { id: "ten_b" }] });
  fetchMock.mockReturnValueOnce(reply(401, { code: "invalid_token", message: "The bearer token is not valid." }));
  await expect(signInWithToken("wrong")).rejects.toMatchObject({ status: 401 });
  expect(session()).toBeNull();
  expect(api.token).toBe("");
  fetchMock.mockReturnValueOnce(reply(200, { principalType: "Credential", user, tenant }));
  await signInWithToken(" arguebotadmin ");
  const unauthorized = vi.fn();
  api.onUnauthorized = unauthorized;
  fetchMock.mockReturnValueOnce(reply(401, { code: "credential_inactive", message: "This credential is disabled." }));
  await expect(api.conversations()).rejects.toMatchObject({ code: "credential_inactive" });
  expect(unauthorized).toHaveBeenCalledOnce();
});

it("refreshes a session token before it expires and signs out on the server", async () => {
  vi.useFakeTimers();
  const { signIn, signOut, session } = await import("./session.js");
  fetchMock.mockReturnValueOnce(reply(201, { token: "abs_first", expiresUtc: new Date(Date.now() + 10 * 60000).toISOString(), user, tenant }));
  await signIn("admin@arguebot", "password");
  fetchMock.mockReturnValueOnce(reply(200, { token: "abs_second", expiresUtc: new Date(Date.now() + 70 * 60000).toISOString(), user, tenant }));
  await vi.advanceTimersByTimeAsync(5 * 60000 + 10);
  expect(fetchMock.mock.calls[1][0]).toBe("/v2.0/api/token/refresh");
  expect(session().token).toBe("abs_second");
  fetchMock.mockReturnValueOnce(reply(200, {}));
  await signOut();
  expect(fetchMock.mock.calls[2][1].method).toBe("DELETE");
  expect(session()).toBeNull();
  expect(sessionStorage.getItem("arguebot.session")).toBeNull();
});
