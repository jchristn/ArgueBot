import { test, expect } from "./fixtures.js";

// Sign in with the password form so the Sessions table has a row for this browser.
test.use({ signedIn: false });

// Clicking a table row opens that object's edit modal, or a read-only details modal when it has nothing to edit.
test("rows open their edit or details modal", async ({ page, request }) => {
  const stamp = Date.now();
  expect((await request.post("/v2.0/api/me/credentials", { data: { name: "Row click " + stamp, active: true, revision: 0 } })).ok()).toBe(true);
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Row click " + stamp } })).json();
  expect((await request.post(`/v2.0/api/conversations/${conversation.id}/shares`, { data: { expiresInHours: 24, includeToolCalls: false } })).ok()).toBe(true);
  try {
    await page.goto("/dashboard/account");
    await page.getByLabel("Email", { exact: true }).fill("admin@arguebot");
    await page.getByLabel("Password", { exact: true }).fill("password");
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Account", exact: true })).toBeVisible();
    await page.getByRole("row").filter({ hasText: "Row click " + stamp }).getByText("Row click " + stamp, { exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Rename", exact: true })).toBeVisible();
    await page.keyboard.press("Escape");
    await page.locator("section").filter({ has: page.getByRole("heading", { name: "Sessions", exact: true }) }).locator("tbody tr").first().locator("td").nth(3).click();
    const session = page.getByRole("dialog", { name: "Session details", exact: true });
    await expect(session).toContainText("Address");
    await page.keyboard.press("Escape");
    await expect(session).toHaveCount(0);

    await page.goto("/dashboard/audit");
    await page.locator("table.audit-table tbody tr").first().locator("td").first().click();
    const event = page.getByRole("dialog", { name: "Audit event", exact: true });
    await expect(event).toContainText("Event ID");
    await page.keyboard.press("Escape");
    await expect(event).toHaveCount(0);

    await page.goto("/dashboard/conversations/" + conversation.id);
    await page.getByRole("button", { name: "Share", exact: true }).click();
    const share = page.getByRole("dialog", { name: "Share a read-only link" });
    await share.locator("tbody tr").first().locator("td").nth(2).click();
    const details = page.getByRole("dialog", { name: "Share link details", exact: true });
    await expect(details).toContainText("Views");
    await expect(details.getByRole("button", { name: "Revoke", exact: true })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(details).toHaveCount(0);
  } finally {
    await request.delete(`/v1.0/api/conversations/${conversation.id}`);
  }
});
