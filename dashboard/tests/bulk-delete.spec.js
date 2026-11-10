import { test, expect } from "./fixtures.js";

// Select several conversations, delete them together, and keep the ones left unselected.
test("conversations: select several and delete them together", async ({ page, request }) => {
  const stamp = "Bulk " + Date.now();
  const created = [];
  for (const suffix of ["one", "two", "keep"]) created.push(await (await request.post("/v1.0/api/conversations", { data: { title: `${stamp} ${suffix}` } })).json());
  try {
    await page.goto("/dashboard/conversations");
    await page.getByRole("searchbox").fill(stamp);
    await expect(page.getByRole("row").filter({ hasText: stamp })).toHaveCount(3);
    await expect(page.getByRole("button", { name: /Delete multiple/ })).toHaveCount(0);
    await page.getByRole("checkbox", { name: `Select ${stamp} one`, exact: true }).check();
    await page.getByRole("checkbox", { name: `Select ${stamp} two`, exact: true }).check();
    await page.getByRole("button", { name: "Delete multiple (2)", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Delete multiple", exact: true });
    await expect(dialog).toContainText(`${stamp} one`);
    await expect(dialog).toContainText(`${stamp} two`);
    await expect(dialog).not.toContainText(`${stamp} keep`);
    await dialog.getByRole("button", { name: "Delete 2 items", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("row").filter({ hasText: stamp })).toHaveCount(1);
    await expect(page.getByRole("row").filter({ hasText: `${stamp} keep` })).toHaveCount(1);
    await expect(page.getByRole("button", { name: /Delete multiple/ })).toHaveCount(0);
    expect((await request.get(`/v1.0/api/conversations/${created[0].id}`)).status()).toBe(404);
    expect((await request.get(`/v1.0/api/conversations/${created[1].id}`)).status()).toBe(404);
  } finally {
    for (const item of created) await request.delete(`/v1.0/api/conversations/${item.id}`);
  }
});

// The select-all box in the header selects every selectable row; factory prompts have no box.
test("prompts: factory defaults cannot be selected for deletion", async ({ page }) => {
  await page.goto("/dashboard/prompts");
  const factory = page.getByRole("row").filter({ hasText: "Factory" }).first();
  await expect(factory).toBeVisible();
  await expect(factory.getByRole("checkbox")).toHaveCount(0);
});
