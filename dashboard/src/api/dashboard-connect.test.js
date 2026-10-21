import { afterEach, expect, it, vi } from "vitest";

afterEach(() => { vi.resetModules(); vi.restoreAllMocks(); sessionStorage.clear(); history.replaceState(null, "", "/"); });
it("opens the local workspace without enrollment and clears obsolete credentials", async () => {
  vi.resetModules();
  history.replaceState(null, "", "/dashboard#connect=obsolete");
  sessionStorage.setItem("arguebot.session", "obsolete-session");
  const request = vi.fn().mockResolvedValue({ status: "ready" });
  const api = { token: "obsolete-session", request };
  vi.doMock("./client.js", () => ({ api }));
  const { connectDashboard } = await import("./dashboard-connect.js");
  const first = connectDashboard();
  expect(connectDashboard()).toBe(first);
  await expect(first).resolves.toMatchObject({ status: "ready" });
  expect(request).toHaveBeenCalledOnce();
  expect(request).toHaveBeenCalledWith("/health");
  expect(api.token).toBe("");
  expect(sessionStorage.getItem("arguebot.session")).toBeNull();
  expect(location.hash).toBe("");
});
