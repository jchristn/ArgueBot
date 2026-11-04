import { test, expect } from "@playwright/test";

test("transcript cards collapse, jump navigation works and only the feed scrolls", async ({ page, request }) => {
  test.setTimeout(90000);
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Transcript navigation " + Date.now() } })).json();
  const root = `/v1.0/api/conversations/${conversation.id}/messages`;
  for (let index = 1; index <= 14; index++) {
    const body = `Note ${index} heading\n\n` + Array.from({ length: 12 }, (_, line) => `Line ${line + 1} of note ${index} with enough words to wrap across the card.`).join("\n\n");
    expect((await request.post(root, { data: { action: "note", text: body, idempotencyKey: `nav-${conversation.id}-${index}` } })).ok()).toBeTruthy();
  }
  try {
    for (const [width, height] of [[1280, 800], [390, 760]]) {
      await page.setViewportSize({ width, height });
      await page.goto(`/dashboard/conversations/${conversation.id}`);
      const feed = page.locator(".message-feed");
      await expect(feed.locator("article.message")).toHaveCount(14);
      // The route itself must not scroll; the feed is the single scrolling region.
      const scrollers = await page.evaluate(() => {
        const route = document.querySelector(".route-content");
        const feed = document.querySelector(".message-feed");
        return { route: route.scrollHeight - route.clientHeight, feed: feed.scrollHeight - feed.clientHeight };
      });
      expect(scrollers.route).toBeLessThanOrEqual(1);
      expect(scrollers.feed).toBeGreaterThan(100);

      const first = feed.locator("article.message").first();
      const toggle = first.getByRole("button", { name: /^Collapse /, exact: false });
      await expect(toggle).toHaveAttribute("aria-expanded", "true");
      const tall = (await first.boundingBox()).height;
      await toggle.click();
      await expect(first.getByRole("button", { name: /^Expand / })).toHaveAttribute("aria-expanded", "false");
      await expect(first.locator(".message-preview")).toContainText("Note 1 heading");
      expect((await first.boundingBox()).height).toBeLessThan(tall / 3);
      await first.getByRole("button", { name: /^Expand / }).click();
      await expect(first.locator(".markdown")).toContainText("Line 12 of note 1");

      const toolbar = page.getByRole("toolbar", { name: "Transcript navigation" });
      await toolbar.getByRole("button", { name: "Collapse all", exact: true }).click();
      await expect(feed.locator("article.message.collapsed")).toHaveCount(14);
      expect(await page.evaluate(() => { const feed = document.querySelector(".message-feed"); return feed.scrollHeight - feed.clientHeight; })).toBeLessThan(scrollers.feed / 3);
      await page.screenshot({ path: `../.run/screenshots/transcript-collapsed-${width}.png` });
      await toolbar.getByRole("button", { name: "Expand all", exact: true }).click();
      await expect(feed.locator("article.message.collapsed")).toHaveCount(0);

      await toolbar.getByRole("combobox", { name: "Jump to turn…" }).selectOption({ index: 10 });
      const target = feed.locator("article.message").nth(9);
      await expect(target).toBeInViewport();
      await expect(target.getByRole("button", { name: /^Collapse / })).toBeFocused();
      await page.screenshot({ path: `../.run/screenshots/transcript-jump-${width}.png` });
    }
  } finally {
    await request.delete(`/v1.0/api/conversations/${conversation.id}`);
  }
});
