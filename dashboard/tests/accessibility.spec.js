import { test, expect } from "./fixtures.js";
import { startProviderFixture } from "./provider-fixture.js";

// Lightweight structural audit run inside the page. It complements, and does not replace, a manual screen-reader pass.
function audit() {
  const problems = [];
  const visible = element => { const style = getComputedStyle(element); const box = element.getBoundingClientRect(); return style.visibility !== "hidden" && style.display !== "none" && box.width > 0 && box.height > 0; };
  const named = element => {
    if ((element.getAttribute("aria-label") || "").trim()) return true;
    const labelledby = element.getAttribute("aria-labelledby");
    if (labelledby && labelledby.split(/\s+/).some(id => document.getElementById(id)?.textContent.trim())) return true;
    if (element.id && document.querySelector(`label[for="${CSS.escape(element.id)}"]`)?.textContent.trim()) return true;
    if (element.closest("label")?.textContent.trim()) return true;
    if ((element.getAttribute("title") || "").trim()) return true;
    if (["BUTTON", "A", "SUMMARY"].includes(element.tagName) && element.textContent.trim()) return true;
    return element.tagName === "INPUT" && ["submit", "button"].includes(element.type) && element.value.trim();
  };
  for (const element of document.querySelectorAll("button, a[href], input:not([type=hidden]), select, textarea, summary"))
    if (visible(element) && !named(element)) problems.push(`unnamed ${element.tagName.toLowerCase()}: ${element.outerHTML.slice(0, 120)}`);
  const ids = new Map();
  for (const element of document.querySelectorAll("[id]")) ids.set(element.id, (ids.get(element.id) || 0) + 1);
  for (const [id, count] of ids) if (count > 1) problems.push(`duplicate id ${id} (${count})`);
  if (document.querySelectorAll("main").length !== 1) problems.push(`expected one main landmark, found ${document.querySelectorAll("main").length}`);
  if ([...document.querySelectorAll("h1")].filter(visible).length !== 1) problems.push(`expected one visible h1, found ${[...document.querySelectorAll("h1")].filter(visible).length}`);
  for (const image of document.querySelectorAll("img")) if (!image.hasAttribute("alt")) problems.push(`image without alt: ${image.src}`);
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent.trim();
    if (/^[a-z]+(\.[a-zA-Z_]+){1,4}$/.test(text) && node.parentElement && visible(node.parentElement) && !node.parentElement.closest("code, pre, .markdown, option")) problems.push(`raw translation key: ${text}`);
  }
  if (document.documentElement.scrollWidth > window.innerWidth + 1) problems.push(`horizontal overflow ${document.documentElement.scrollWidth} > ${window.innerWidth}`);
  for (const region of document.querySelectorAll(".debate-sidebar, .message-feed, .modal-body, dialog")) if (visible(region) && region.scrollWidth > region.clientWidth + 1) problems.push(`clipped ${region.className || region.tagName}: ${region.scrollWidth} > ${region.clientWidth}`);
  return problems;
}

test("structural accessibility, right-to-left and expansion review across routes and a live debate", async ({ page, request }) => {
  test.setTimeout(180000);
  const fixture = await startProviderFixture();
  try {
    const post = async (path, data) => { const response = await request.post(path, { data }); expect(response.ok(), await response.text()).toBeTruthy(); return response.json(); };
    const profiles = [];
    for (const name of ["Audit A", "Audit B", "Audit moderator"]) profiles.push(await post("/v2.0/api/participants", { name: name + " " + Date.now(), kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent" }));
    const conversation = await post("/v2.0/api/conversations", { title: "Accessibility review" });
    const run = await post(`/v2.0/api/conversations/${conversation.id}/runs`, { protocolVersion: 2, participantIds: profiles.slice(0, 2).map(profile => profile.id), orchestratorId: profiles[2].id, maxRounds: 1, interventionSeconds: 0, idempotencyKey: "audit-" + Date.now() });
    await expect.poll(async () => (await (await request.get(`/v2.0/api/conversations/${conversation.id}/runs/${run.id}`)).json()).state, { timeout: 30000 }).toBe("completed");
    const routes = ["home", "conversations", "participants", "settings", "history", "explorer", "account", "users", "tenants", "audit", `conversations/${conversation.id}`];
    const findings = [];
    for (const locale of ["en", "ar-XB", "en-XA"]) {
      await page.addInitScript(locale => localStorage.setItem("arguebot.locale", locale), locale);
      for (const width of [390, 1280]) {
        await page.setViewportSize({ width, height: 900 });
        for (const route of routes) {
          await page.goto("/dashboard/" + route);
          await expect(page.locator("main")).toBeVisible();
          await page.waitForTimeout(400);
          if (route.startsWith("conversations/")) await expect(page.locator(".message, .timeline-row").first()).toBeVisible();
          expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === "ar-XB" ? "rtl" : "ltr");
          for (const problem of await page.evaluate(audit)) findings.push(`${locale} ${width}px /${route.replace(conversation.id, "{id}")}: ${problem}`);
          if (locale !== "en" && (route === "participants" || route.startsWith("conversations/"))) await page.screenshot({ path: `../.run/screenshots/a11y-${locale}-${route.startsWith("conversations/") ? "workspace" : route}-${width}.png`, fullPage: true });
        }
      }
    }
    expect(findings, findings.join("\n")).toEqual([]);
  } finally { await fixture.stop(); }
});
