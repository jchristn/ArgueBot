import { test, expect } from "./fixtures.js";

test("card view: transcript cards collapse, jump navigation works and only the feed scrolls", async ({ page, request }) => {
  test.setTimeout(90000);
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Transcript navigation " + Date.now() } })).json();
  const root = `/v1.0/api/conversations/${conversation.id}/messages`;
  for (let index = 1; index <= 14; index++) {
    const body = `Note ${index} heading\n\n` + Array.from({ length: 12 }, (_, line) => `Line ${line + 1} of note ${index} with enough words to wrap across the card.`).join("\n\n");
    expect((await request.post(root, { data: { action: "note", text: body, idempotencyKey: `nav-${conversation.id}-${index}` } })).ok()).toBeTruthy();
  }
  try {
    await page.addInitScript(() => localStorage.setItem("arguebot.transcriptView", "cards"));
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

test("timeline view shows one compact row per turn, groups tool calls and expands in place", async ({ page, request }) => {
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Timeline view " + Date.now() } })).json();
  const root = `/v1.0/api/conversations/${conversation.id}/messages`;
  for (let index = 1; index <= 24; index++) {
    const body = `Turn ${index} opening line\n\n` + Array.from({ length: 10 }, (_, line) => `Detail ${line + 1} of turn ${index}.`).join("\n\n");
    await request.post(root, { data: { action: "note", text: body, idempotencyKey: `timeline-${conversation.id}-${index}` } });
  }
  try {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    const toolbar = page.getByRole("toolbar", { name: "Transcript navigation" });
    await expect(toolbar.getByRole("radio", { name: "Timeline" })).toHaveAttribute("aria-checked", "true");
    const rows = page.locator(".timeline-row");
    await expect(rows).toHaveCount(24);
    // Compact rows: far more turns fit on screen than cards would show.
    const height = (await rows.first().boundingBox()).height;
    expect(height).toBeLessThan(40);
    const visible = await page.evaluate(() => { const feed = document.querySelector(".message-feed").getBoundingClientRect(); return [...document.querySelectorAll(".timeline-row")].filter(row => { const box = row.getBoundingClientRect(); return box.top >= feed.top && box.bottom <= feed.bottom; }).length; });
    expect(visible).toBeGreaterThanOrEqual(12);
    const first = rows.first();
    await expect(first.locator(".timeline-preview")).toContainText("opening line");
    await first.getByRole("button").first().click();
    await expect(first.getByRole("button").first()).toHaveAttribute("aria-expanded", "true");
    await expect(first.locator(".markdown")).toContainText("Detail 10 of turn");
    await page.screenshot({ path: "../.run/screenshots/timeline-1280.png" });
    await toolbar.getByRole("button", { name: "Expand all", exact: true }).click();
    await expect(page.locator(".timeline-row.open")).toHaveCount(24);
    await toolbar.getByRole("button", { name: "Collapse all", exact: true }).click();
    await expect(page.locator(".timeline-row.open")).toHaveCount(0);
    await toolbar.getByRole("radio", { name: "Cards" }).click();
    await expect(page.locator("article.message")).toHaveCount(24);
    await page.reload();
    await expect(page.getByRole("toolbar", { name: "Transcript navigation" }).getByRole("radio", { name: "Cards" })).toHaveAttribute("aria-checked", "true");
    await page.getByRole("toolbar", { name: "Transcript navigation" }).getByRole("radio", { name: "Timeline" }).click();
  } finally {
    await request.delete(`/v1.0/api/conversations/${conversation.id}`);
  }
});

test("a turn longer than the transcript preview reads in full inline and in its own window, with markdown", async ({ page, request }) => {
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Long turn " + Date.now() } })).json();
  const body = "## Findings\n\n" + Array.from({ length: 120 }, (_, line) => `Paragraph ${line + 1} explains one finding in enough words to be long.`).join("\n\n") + "\n\n| Configuration | Establishes |\n|---|---|\n| Prompt-only | An instruction |\n\nLONG_TURN_FINAL_SENTENCE.";
  await request.post(`/v1.0/api/conversations/${conversation.id}/messages`, { data: { action: "note", text: body, idempotencyKey: "long-" + conversation.id } });
  try {
    const listed = (await (await request.get(`/v1.0/api/conversations/${conversation.id}/messages?pageSize=25`)).json()).items[0];
    expect(listed.contentTruncated).toBe(true);
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    const row = page.locator(".timeline-row").first();
    await row.getByRole("button").first().click();
    await expect(row.locator(".markdown")).toContainText("LONG_TURN_FINAL_SENTENCE.");
    await expect(row.locator(".markdown table td").first()).toHaveText("Prompt-only");
    await expect(row.locator(".markdown h2")).toHaveText("Findings");
    await row.getByRole("button", { name: "Open in window", exact: true }).click();
    const reader = page.getByRole("dialog", { name: "Read full message" });
    await expect(reader.locator(".reader-body")).toContainText("LONG_TURN_FINAL_SENTENCE.");
    await expect(reader.locator(".reader-body table")).toBeVisible();
    await page.screenshot({ path: "../.run/screenshots/message-reader.png" });
    await page.keyboard.press("Escape");
    await page.getByRole("toolbar", { name: "Transcript navigation" }).getByRole("radio", { name: "Cards" }).click();
    await expect(page.locator("article.message .markdown")).toContainText("LONG_TURN_FINAL_SENTENCE.");
    await page.getByRole("toolbar", { name: "Transcript navigation" }).getByRole("radio", { name: "Timeline" }).click();
  } finally {
    await request.delete(`/v1.0/api/conversations/${conversation.id}`);
  }
});

test("a working agent with no visible output shows a live row, and new turns appear even after scrolling up", async ({ page, request }) => {
  test.setTimeout(90000);
  const { default: path } = await import("node:path");
  const profile = await (await request.post("/v1.0/api/participants", { data: { name: "Silent worker " + Date.now(), kind: "cli", provider: "claude", executable: "dotnet", arguments: [path.resolve("../src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll"), "diagnostic-timeout"], timeoutSeconds: 120 } })).json();
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Live row " + Date.now() } })).json();
  const root = `/v1.0/api/conversations/${conversation.id}`;
  for (let index = 1; index <= 20; index++) await request.post(`${root}/messages`, { data: { action: "note", text: `Earlier note ${index}\n\n` + "Context line.\n\n".repeat(6), idempotencyKey: `live-${conversation.id}-${index}` } });
  const run = await (await request.post(`${root}/runs`, { data: { mode: "followup", question: "Fictional long silent turn", participantIds: [profile.id], idempotencyKey: "live-" + conversation.id } })).json();
  try {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    const live = page.locator(".live-turn");
    await expect(live).toContainText(profile.name);
    await expect(live).toContainText("Nothing is visible yet");
    await expect(live.locator("time")).toHaveText(/^\d+:\d\d$/);
    const first = await live.locator("time").textContent();
    await expect(live.locator("time")).not.toHaveText(first, { timeout: 5000 });
    await page.screenshot({ path: "../.run/screenshots/timeline-live.png" });
    await request.post(`${root}/runs/${run.id}/actions`, { data: { action: "stop", idempotencyKey: "live-stop-early-" + conversation.id } });
    await expect(live).toHaveCount(0, { timeout: 30000 });
    // Scroll away from the newest turn, then add one: it must still appear without paging.
    await page.locator(".message-feed").evaluate(feed => { feed.scrollTop = 0; feed.dispatchEvent(new Event("scroll")); });
    const late = await request.post(`${root}/messages`, { data: { action: "note", text: "ARRIVED_WHILE_SCROLLED", idempotencyKey: "live-late-" + conversation.id } });
    expect(late.status(), await late.text()).toBeLessThan(300);
    await expect(page.locator(".timeline-row").filter({ hasText: "ARRIVED_WHILE_SCROLLED" })).toHaveCount(1, { timeout: 10000 });
    await expect(page.getByRole("button", { name: "Newer messages available" })).toHaveCount(0);
  } finally {
    await request.post(`${root}/runs/${run.id}/actions`, { data: { action: "stop", idempotencyKey: "live-stop-" + conversation.id } });
    for (let attempt = 0; attempt < 40; attempt++) { const latest = (await (await request.get(`${root}/runs`)).json()).at(-1); if (["cancelled", "completed", "failed"].includes(latest.state)) break; await new Promise(resolve => setTimeout(resolve, 500)); }
    await request.delete(root);
    await request.delete(`/v1.0/api/participants/${profile.id}`);
  }
});
