import { test, expect } from "@playwright/test";

test("versioned dashboard assets are reusable while HTML and API data stay fresh", async ({ page }) => {
  const html = await page.goto("/dashboard/history");
  expect(html.headers()["cache-control"]).toBe("no-store");
  const icon = page.locator(".wordmark img");
  await expect(icon).toBeVisible();
  const src = await icon.getAttribute("src");
  expect(src).toMatch(/^\/assets\/icon-[A-Za-z0-9_-]+\.png$/);
  const asset = await page.request.get(src);
  expect(asset.status()).toBe(200);
  expect(asset.headers()["cache-control"]).toBe("public, max-age=31536000, immutable");
  const script = await page.locator('script[type="module"]').getAttribute("src");
  expect((await page.request.get(script)).headers()["cache-control"]).toBe("public, max-age=31536000, immutable");
  const api = await page.request.get("/v1.0/api/conversations");
  expect(api.headers()["cache-control"]).toBe("no-store");
  expect((await page.request.get("/assets/not-present-fixture.png")).status()).toBe(404);
  // A real second document navigation should reuse the browser's downloaded icon.
  const before = await (await page.request.get("/v1.0/api/request-history?search=" + encodeURIComponent(src))).json();
  await page.goto("/dashboard/participants");
  await expect(page.locator(".wordmark img")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Participants", exact: true })).toBeVisible();
  const after = await (await page.request.get("/v1.0/api/request-history?search=" + encodeURIComponent(src))).json();
  expect(after.totalCount).toBe(before.totalCount);
});
