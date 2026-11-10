import { test, expect } from "./fixtures.js";

const admin = { Authorization: "Bearer arguebotadmin" };
const created = async (response) => { expect(response.ok()).toBe(true); return response.json(); };

// Select several conversations (one by one and with a Shift range), delete them together, and keep the ones left unselected.
test("conversations: select several and delete them together", async ({ page, request }) => {
  const stamp = "Bulk " + Date.now();
  const records = [];
  for (const suffix of ["a", "b", "c", "d", "keep"]) records.push(await created(await request.post("/v1.0/api/conversations", { data: { title: `${stamp} ${suffix}` } })));
  try {
    await page.goto("/dashboard/conversations");
    await page.getByRole("searchbox").fill(stamp);
    await expect(page.getByRole("row").filter({ hasText: stamp })).toHaveCount(5);
    await expect(page.getByRole("button", { name: /Delete multiple/ })).toHaveCount(0);
    // Newest first: keep, d, c, b, a. Click d, then Shift+click a to select the range d..a.
    await page.getByRole("checkbox", { name: `Select ${stamp} d`, exact: true }).click();
    await page.getByRole("checkbox", { name: `Select ${stamp} a`, exact: true }).click({ modifiers: ["Shift"] });
    await expect(page.getByRole("checkbox", { name: `Select ${stamp} b`, exact: true })).toBeChecked();
    await expect(page.getByRole("checkbox", { name: `Select ${stamp} keep`, exact: true })).not.toBeChecked();
    await page.getByRole("button", { name: "Delete multiple (4)", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Delete multiple", exact: true });
    for (const suffix of ["a", "b", "c", "d"]) await expect(dialog).toContainText(`${stamp} ${suffix}`);
    await expect(dialog).not.toContainText(`${stamp} keep`);
    await dialog.getByRole("button", { name: "Delete 4 items", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("row").filter({ hasText: stamp })).toHaveCount(1);
    await expect(page.getByRole("row").filter({ hasText: `${stamp} keep` })).toHaveCount(1);
    await expect(page.getByRole("button", { name: /Delete multiple/ })).toHaveCount(0);
    for (const record of records.slice(0, 4)) expect((await request.get(`/v1.0/api/conversations/${record.id}`)).status()).toBe(404);
  } finally {
    for (const record of records) await request.delete(`/v1.0/api/conversations/${record.id}`);
  }
});

// Every table with Delete has a narrow checkbox column with a box on every row, and the name column keeps its width.
test("every table with Delete has a narrow checkbox column with a box on every row", async ({ page }) => {
  for (const route of ["conversations", "participants", "resources", "prompts", "history", "users", "tenants", "questions", "account"]) {
    await page.goto("/dashboard/" + route);
    if (route === "questions") await page.getByLabel("Status", { exact: true }).selectOption("");
    const header = page.locator("th.select-cell").first();
    if (route === "questions" && await page.locator("tbody tr").count() === 0) continue;
    await expect(header, route).toBeVisible();
    const table = header.locator("xpath=ancestor::table[1]");
    const rows = await table.locator("tbody tr").count();
    expect(await table.locator("tbody td.select-cell input[type=checkbox]").count(), route).toBe(rows);
    const width = (await header.boundingBox()).width;
    expect(width, route + " checkbox column width").toBeLessThan(48);
    if (rows > 0) expect((await table.locator("tbody tr").first().locator("td").nth(1).boundingBox()).width, route + " name column width").toBeGreaterThanOrEqual(150);
  }
});

// Factory prompts show a disabled box that says why; a workspace copy can be selected and deleted.
test("prompts: factory defaults are disabled with the reason, copies can be deleted", async ({ page, request }) => {
  const factory = (await (await request.get("/v2.0/api/prompts")).json()).find(item => item.factory);
  const duplicate = await created(await request.post(`/v2.0/api/prompts/${factory.id}/duplicate`, { data: {} }));
  const copy = await created(await request.put(`/v2.0/api/prompts/${duplicate.id}`, { data: { ...duplicate, name: "Bulk prompt " + Date.now() } }));
  try {
    await page.goto("/dashboard/prompts");
    const factoryRow = page.getByRole("row").filter({ hasText: factory.name }).first();
    await expect(factoryRow.getByRole("checkbox")).toBeDisabled();
    await expect(factoryRow.locator("td.select-cell")).toHaveAttribute("title", "Factory prompts are read-only and cannot be deleted.");
    await page.getByRole("row").filter({ hasText: copy.name }).getByRole("checkbox").check();
    await page.getByRole("button", { name: "Delete multiple (1)", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Delete", exact: true });
    await dialog.getByRole("button", { name: "Delete 1 item", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("row").filter({ hasText: copy.name })).toHaveCount(0);
    expect((await request.get(`/v2.0/api/prompts/${copy.id}`)).status()).toBe(404);
  } finally {
    const current = await request.get(`/v2.0/api/prompts/${copy.id}`);
    if (current.ok()) await request.delete(`/v2.0/api/prompts/${copy.id}?revision=${(await current.json()).revision}`);
  }
});

// Users: your own account is disabled with the reason; deleting several asks you to type delete first.
test("users: select several and delete them after typing delete", async ({ page, request }) => {
  const stamp = Date.now();
  const users = [];
  for (const index of [1, 2]) users.push(await created(await request.post("/v2.0/api/users", { headers: admin, data: { firstName: "Bulk", lastName: `User ${index} ${stamp}`, email: `bulk-${index}-${stamp}@example.test`, password: "bulk-password-1" } })));
  await page.goto("/dashboard/users");
  const self = page.getByRole("row").filter({ hasText: "admin@arguebot" });
  await expect(self.getByRole("checkbox")).toBeDisabled();
  for (const user of users) await page.getByRole("row").filter({ hasText: user.email }).getByRole("checkbox").check();
  await page.getByRole("button", { name: "Delete multiple (2)", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Delete multiple", exact: true });
  const remove = dialog.getByRole("button", { name: "Delete 2 items", exact: true });
  await expect(remove).toBeDisabled();
  await dialog.getByLabel("Type delete to confirm").fill("delete");
  await remove.click();
  await expect(dialog).toHaveCount(0);
  for (const user of users) await expect(page.getByRole("row").filter({ hasText: user.email })).toHaveCount(0);
});

// Tenants: the tenant you are signed in to is disabled with the reason; deleting several asks you to type delete first.
test("tenants: select several and delete them after typing delete", async ({ page, request }) => {
  const stamp = Date.now();
  const tenants = [];
  for (const index of [1, 2]) tenants.push(await created(await request.post("/v2.0/api/tenants", { headers: admin, data: { name: `Bulk tenant ${index} ${stamp}`, active: true } })));
  await page.goto("/dashboard/tenants");
  const own = page.getByRole("row").filter({ hasText: "Yours" });
  await expect(own.getByRole("checkbox")).toBeDisabled();
  for (const tenant of tenants) await page.getByRole("row").filter({ hasText: tenant.name }).getByRole("checkbox").check();
  await page.getByRole("button", { name: "Delete multiple (2)", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Delete multiple", exact: true });
  await dialog.getByLabel("Type delete to confirm").fill("delete");
  await dialog.getByRole("button", { name: "Delete 2 items", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  for (const tenant of tenants) await expect(page.getByRole("row").filter({ hasText: tenant.name })).toHaveCount(0);
});
