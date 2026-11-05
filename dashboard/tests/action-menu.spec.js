import { test, expect } from "./fixtures.js";

test("table rows use an Actions menu that stays in the viewport and renames conversations", async ({ page, request }) => {
  test.setTimeout(120000);
  const suffix = Date.now().toString();
  const created = [];
  for (let index = 0; index < 30; index++) created.push(await (await request.post("/v1.0/api/conversations", { data: { title: `Menu check ${suffix} ${String(index).padStart(2, "0")}` } })).json());
  try {
    for (const [width, height] of [[1280, 720], [390, 700]]) {
      await page.setViewportSize({ width, height });
      await page.goto("/dashboard/conversations");
      await page.getByRole("searchbox").fill("Menu check " + suffix);
      const table = page.getByRole("table");
      await expect(table.getByRole("columnheader", { name: "Title", exact: true })).toBeVisible();
      await expect(table.getByRole("columnheader", { name: "Actions", exact: true })).toBeVisible();
      await expect(page.getByText("Record actions", { exact: true })).toHaveCount(0);
      const rows = table.getByRole("row").filter({ hasText: "Menu check " + suffix });
      await expect(rows).toHaveCount(25);
      // The last visible row on the page sits near the bottom: its menu must flip above and stay fully visible.
      const last = rows.last();
      await last.scrollIntoViewIfNeeded();
      const trigger = last.getByRole("button", { name: /^Actions for / });
      await trigger.click();
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      await expect(menu.getByRole("menuitem")).toHaveText(["Open conversation", "Read transcript", "Share", "Rename", "Pin", "Archive", "View JSON", "Delete"]);
      const box = await menu.boundingBox();
      const viewport = page.viewportSize();
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.y).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
      expect(box.y + box.height).toBeLessThanOrEqual(viewport.height);
      // The menu draws above the table instead of being clipped by its scroll container.
      const topmost = await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.closest("[role=menu]") !== null, { x: box.x + box.width / 2, y: box.y + box.height / 2 });
      expect(topmost).toBeTruthy();
      await expect(menu.getByRole("menuitem").first()).toBeFocused();
      await page.keyboard.press("ArrowDown");
      await expect(menu.getByRole("menuitem", { name: "Read transcript", exact: true })).toBeFocused();
      await page.screenshot({ path: `../.run/screenshots/action-menu-${width}.png` });
      await page.keyboard.press("Escape");
      await expect(menu).toHaveCount(0);
      await expect(trigger).toBeFocused();
      await trigger.click();
      await page.mouse.click(5, 5);
      await expect(page.getByRole("menu")).toHaveCount(0);
    }
    const first = page.getByRole("row").filter({ hasText: created.at(-1).id });
    await first.getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Rename", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Rename conversation" });
    await dialog.getByRole("textbox", { name: "Title", exact: true }).fill(`Renamed ${suffix}`);
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await page.getByRole("searchbox").fill(`Renamed ${suffix}`);
    await expect(page.getByRole("row").filter({ hasText: created.at(-1).id })).toContainText(`Renamed ${suffix}`);
    for (const tab of ["agents", "models", "endpoints", "credentials"]) {
      await page.goto("/dashboard/participants?tab=" + tab);
      const toolbar = page.locator(".tab-toolbar");
      const primary = toolbar.locator(".button.primary");
      const [bar, button] = [await toolbar.boundingBox(), await primary.boundingBox()];
      expect(Math.abs((bar.x + bar.width) - (button.x + button.width)), tab).toBeLessThan(2);
    }
    for (const route of ["participants", "history"]) {
      await page.goto("/dashboard/" + route);
      const header = page.getByRole("columnheader", { name: "Actions", exact: true }).first();
      await expect(header).toBeVisible();
      await page.getByRole("button", { name: /^Actions for / }).first().click();
      await expect(page.getByRole("menu")).toBeVisible();
      await page.keyboard.press("Escape");
    }
  } finally {
    for (const item of created) await request.delete(`/v1.0/api/conversations/${item.id}`);
  }
});
