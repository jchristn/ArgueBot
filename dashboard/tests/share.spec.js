import { test, expect } from "./fixtures.js";

test("a conversation shares a read-only, time-bound link that works signed out until it is revoked", async ({ page, request, browser }) => {
  const title = "Shared transcript " + Date.now();
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title, openingQuestion: "Should this be public?" } })).json();
  expect((await request.post(`/v1.0/api/conversations/${conversation.id}/messages`, { data: { action: "note", text: "A note anyone with the link can read.", idempotencyKey: "share-" + Date.now() } })).ok()).toBe(true);
  await page.goto("/dashboard/conversations/" + conversation.id);
  await page.getByRole("button", { name: "Share", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Share a read-only link" });
  await dialog.getByLabel("Expires", { exact: true }).selectOption({ label: "In 1 day" });
  await dialog.getByRole("button", { name: "Create link", exact: true }).click();
  const url = (await dialog.locator(".share-created code").textContent()).trim();
  expect(url).toMatch(/^https:\/\/127\.0\.0\.1:4420\/share\/[A-Za-z0-9_-]{40,}$/);
  await expect(dialog.getByRole("row").filter({ hasText: "…" + url.slice(-4) })).toContainText("Active");

  const reader = await browser.newContext({ ignoreHTTPSErrors: true, extraHTTPHeaders: {} });
  const outside = await reader.newPage();
  await outside.goto(url);
  await expect(outside.getByRole("heading", { name: title, level: 1 })).toBeVisible();
  await expect(outside.getByText("A note anyone with the link can read.")).toBeVisible();
  await expect(outside.getByRole("note")).toContainText("Shared from ArgueBot (read-only)");
  await expect(outside.getByRole("link", { name: "Back to workspace" })).toHaveCount(0);
  await expect(outside.getByRole("button", { name: "Account and sign out" })).toHaveCount(0);
  expect(await outside.evaluate(() => sessionStorage.getItem("arguebot.session"))).toBeNull();

  const row = dialog.getByRole("row").filter({ hasText: "…" + url.slice(-4) });
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Revoke", exact: true }).click();
  await expect(row).toContainText("Revoked");
  await outside.reload();
  await expect(outside.getByRole("alert")).toContainText("This link is not valid");
  await reader.close();
});
