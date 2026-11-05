import { afterEach, expect, it, vi } from "vitest";

afterEach(() => { vi.resetModules(); vi.restoreAllMocks(); sessionStorage.clear(); history.replaceState(null, "", "/"); });
it("checks the service once, drops obsolete fragments and restores no session when none is stored", async () => {
  history.replaceState(null, "", "/dashboard#connect=obsolete");
  const request = vi.fn().mockResolvedValue({ status: "ready" });
  vi.doMock("./client.js", () => ({ api: { token: "", request } }));
  vi.doMock("./session.js", () => ({ restoreSession: vi.fn().mockResolvedValue(null) }));
  const { connectDashboard } = await import("./dashboard-connect.js");
  const first = connectDashboard();
  expect(connectDashboard()).toBe(first);
  await expect(first).resolves.toBeNull();
  expect(request).toHaveBeenCalledOnce();
  expect(request).toHaveBeenCalledWith("/health");
  expect(location.hash).toBe("");
});
