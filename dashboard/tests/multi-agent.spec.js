import { test, expect } from "@playwright/test";
import { startProviderFixture } from "./provider-fixture.js";

async function post(request, path, data) {
  const response = await request.post(path, { data });
  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}
test("four-agent panel, explicit context, proposals and write-only configuration", async ({ page, request }) => {
  test.setTimeout(90000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  try {
    await page.goto("/dashboard/participants");
    const catalog = page.getByRole("tabpanel");
    await page.getByRole("tab", { name: /^Credentials/ }).click();
    await expect(page).toHaveURL(/tab=credentials/);
    await catalog.getByRole("button", { name: "Add credential", exact: true }).click();
    let dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Gateway " + suffix);
    const guide = dialog.getByRole("region", { name: "How to get this credential" });
    await expect(guide.locator(".command-box code")).toHaveText(["claude setup-token"]);
    await expect(guide.getByRole("button", { name: "Copy command" })).toHaveCount(1);
    await dialog.getByRole("combobox", { name: "Credential kind", exact: true }).selectOption("codex_auth_json");
    await guide.getByRole("radio", { name: "macOS" }).click();
    await expect(guide.locator(".command-box code").first()).toHaveText("mkdir -p ~/.codex-arguebot && CODEX_HOME=~/.codex-arguebot codex login");
    await expect(guide).toContainText("Cmd+Shift+.");
    await guide.getByRole("radio", { name: "Windows" }).click();
    await expect(guide).toContainText("PowerShell");
    await expect(guide.locator(".command-box code").first()).toContainText('$env:CODEX_HOME = "$HOME\\.codex-arguebot"; codex login');
    await expect(guide.locator(".command-box code").last()).toHaveText('Get-Content "$HOME\\.codex-arguebot\\auth.json" -Raw | Set-Clipboard');
    await page.screenshot({ path: "../.run/screenshots/v2-codex-guide-windows.png", fullPage: true });
    await guide.getByRole("radio", { name: "Linux" }).click();
    await expect(guide.locator(".command-box code").last()).toHaveText("xclip -selection clipboard < ~/.codex-arguebot/auth.json");
    await guide.getByRole("radio", { name: "macOS" }).click();
    await page.screenshot({ path: "../.run/screenshots/v2-codex-guide.png", fullPage: true });
    await dialog.getByRole("combobox", { name: "Credential kind", exact: true }).selectOption("http_bearer");
    await expect(dialog.getByRole("textbox", { name: "Header name", exact: true })).toHaveCount(0);
    await expect(guide).toContainText("Authorization: Bearer");
    await dialog.getByRole("combobox", { name: "Credential kind", exact: true }).selectOption("http_query");
    await expect(dialog.getByRole("textbox", { name: "Query parameter name", exact: true })).toBeVisible();
    await dialog.getByRole("combobox", { name: "Credential kind", exact: true }).selectOption("http_header");
    await dialog.getByRole("textbox", { name: "Header name", exact: true }).fill("x-api-key");
    await page.screenshot({ path: "../.run/screenshots/v2-header-credential.png", fullPage: true });
    const secret = dialog.getByLabel("Value", { exact: true });
    await expect(secret).toHaveAttribute("type", "password");
    await secret.fill("fictional-gateway-token");
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(catalog.getByRole("row").filter({ hasText: "Gateway " + suffix })).toContainText("Header key and value x-api-key");
    await expect(catalog.getByRole("row").filter({ hasText: "Gateway " + suffix })).toContainText("Not used yet");
    const gatewayRow = catalog.getByRole("row").filter({ hasText: "Gateway " + suffix });
    await gatewayRow.getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Reveal value", exact: true }).click();
    const reveal = page.getByRole("dialog", { name: "Reveal credential" });
    await expect(reveal.getByRole("alert")).toContainText("Anyone who can see this screen");
    await expect(reveal).not.toContainText("fictional-gateway-token");
    await reveal.getByRole("button", { name: "Reveal", exact: true }).click();
    await expect(reveal.getByLabel("Value", { exact: true })).toHaveValue("fictional-gateway-token");
    await expect(reveal).toContainText("x-api-key");
    await reveal.getByRole("button", { name: "Hide", exact: true }).click();
    await expect(reveal.getByLabel("Value", { exact: true })).toHaveAttribute("type", "password");
    await page.screenshot({ path: "../.run/screenshots/v2-reveal-credential.png", fullPage: true });
    await reveal.getByRole("button", { name: "Close", exact: true }).last().click();
    await expect.poll(async () => (await (await request.get("/v1.0/api/request-history?pageSize=50")).json()).items.some(item => item.path.endsWith("/reveal"))).toBeTruthy();
    const revealEntry = (await (await request.get("/v1.0/api/request-history?pageSize=50")).json()).items.find(item => item.path.endsWith("/reveal"));
    const revealDetail = await (await request.get(`/v1.0/api/request-history/${revealEntry.id}`)).text();
    expect(revealDetail).toContain("[credential body excluded]");
    expect(revealDetail).not.toContain("fictional-gateway-token");
    await catalog.getByRole("button", { name: "Credential encryption keys", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("Earlier keys stay available");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await expect(page.getByRole("dialog").getByRole("button", { name: "Rotate master key", exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1 && document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-keys-${width}-${theme}.png`, fullPage: true });
    }
    await page.getByRole("dialog").getByRole("button", { name: "Rotate master key", exact: true }).click();
    await expect(page.getByRole("dialog").getByRole("status").first()).toContainText("reencrypted");
    const keyDialog = page.getByRole("dialog");
    await keyDialog.getByRole("button", { name: "Retire earlier keys", exact: true }).click();
    await expect(keyDialog.getByRole("alert")).toContainText("can no longer decrypt");
    for (const width of [390, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-retire-keys-${width}-${theme}.png`, fullPage: true });
    }
    await keyDialog.getByRole("button", { name: "Retire keys permanently", exact: true }).click();
    await expect(keyDialog.getByRole("status").last()).toContainText("retired");
    await expect(keyDialog.locator(".server-details dd").nth(1)).toHaveText("1");
    await expect(keyDialog.getByRole("button", { name: "Retire earlier keys", exact: true })).toHaveCount(0);
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    const credentials = await (await request.get("/v2.0/api/configuration/credentials")).json();
    const credential = credentials.find(item => item.name === "Gateway " + suffix);
    expect(JSON.stringify(credentials)).not.toContain("fictional-gateway-token");
    await page.getByRole("tab", { name: /^HTTP Endpoints/ }).click();
    await catalog.getByRole("button", { name: "Add endpoint", exact: true }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Endpoint " + suffix);
    await dialog.getByRole("textbox", { name: "Base URL", exact: true }).fill(fixture.endpoint);
    await dialog.getByRole("button", { name: "Add binding", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Stored credential", exact: true }).selectOption({ label: `Gateway ${suffix} (Header key and value)` });
    await expect(dialog.getByRole("combobox", { name: "Binding type", exact: true })).toHaveValue("header");
    await expect(dialog.getByRole("combobox", { name: "Binding type", exact: true })).toBeDisabled();
    await expect(dialog.getByRole("textbox", { name: "Header or query name", exact: true })).toHaveValue("x-api-key");
    await page.screenshot({ path: "../.run/screenshots/v2-endpoint-typed-binding.png", fullPage: true });
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    const endpoints = await (await request.get("/v2.0/api/configuration/endpoints")).json();
    const endpoint = endpoints.find(item => item.name === "Endpoint " + suffix);
    await catalog.getByRole("row").filter({ hasText: "Endpoint " + suffix }).getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Preview requests", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText(fixture.endpoint + "/api/chat");
    await expect(page.getByRole("dialog")).toContainText("No endpoint is contacted");
    await expect(page.getByRole("dialog")).toContainText("x-api-key: [redacted]");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await expect(page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last()).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-preview-${width}-${theme}.png`, fullPage: true });
    }
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    await catalog.getByRole("row").filter({ hasText: "Endpoint " + suffix }).getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Check connection / list models" }).click();
    await expect(page.getByRole("dialog")).toContainText("Connection check succeeded");
    await expect(page.getByRole("dialog")).toContainText("multi-agent");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { localStorage.setItem("arguebot.theme", theme); document.documentElement.dataset.theme = theme; }, theme);
      await expect(page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last()).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1 && document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-discovery-${width}-${theme}.png`, fullPage: true });
    }
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    await page.getByRole("tab", { name: /^Models/ }).click();
    await catalog.getByRole("button", { name: "Add model", exact: true }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Model " + suffix);
    await dialog.getByRole("combobox", { name: "HTTP endpoint", exact: true }).selectOption(endpoint.id);
    await dialog.getByRole("textbox", { name: "Provider model ID", exact: true }).fill("multi-agent");
    await dialog.getByLabel("Generated-token limit (optional)", { exact: true }).fill("2048");
    await dialog.getByLabel("Temperature (optional)", { exact: true }).fill("0.25");
    await dialog.getByLabel("Top-p (optional)", { exact: true }).fill("0.9");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await dialog.getByLabel("Top-p (optional)", { exact: true }).scrollIntoViewIfNeeded();
      await expect(dialog.getByLabel("Generated-token limit (optional)", { exact: true })).toHaveValue("2048");
      await expect(dialog).not.toContainText("catalog.maxOutputTokens.help");
      await expect(dialog).not.toContainText("catalog.temperature.help");
      await expect(dialog).not.toContainText("catalog.topP.help");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-generation-${width}-${theme}.png`, fullPage: true });
    }
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    const models = await (await request.get("/v2.0/api/configuration/models")).json();
    const model = models.find(item => item.name === "Model " + suffix);
    expect(model.generation).toEqual({ maxOutputTokens: 2048, temperature: 0.25, topP: 0.9 });
    await catalog.getByRole("row").filter({ hasText: "Model " + suffix }).getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Completion and tool checks", exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText("may incur provider cost");
    await page.getByRole("dialog").getByRole("button", { name: "Invoke model check (may incur cost)", exact: true }).click();
    await expect(page.getByRole("dialog").locator("article").first()).toContainText("Completed");
    await page.getByRole("dialog").getByRole("checkbox", { name: "Include a harmless tool round trip", exact: true }).check();
    await page.getByRole("dialog").getByRole("button", { name: "Invoke model check (may incur cost)", exact: true }).click();
    await expect(page.getByRole("dialog").locator("article").first()).toContainText("tool_probe_not_observed");
    await expect(page.getByRole("dialog").locator("article").first()).toContainText("Model revision 1 · transport revision 1");
    await expect(page.getByRole("dialog")).not.toContainText("{{");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().scrollIntoViewIfNeeded();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1 && document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-model-check-${width}-${theme}.png`, fullPage: true });
    }
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    const profiles = [];
    for (const name of ["Claude", "Codex", "Gemini", "DeepSeek"])
      profiles.push(await post(request, "/v2.0/api/participants", { name: name + " " + suffix, modelProfileId: model.id }));
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Four-agent rollout " + suffix });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    await page.getByRole("button", { name: "Start another debate", exact: true }).click();
    dialog = page.getByRole("dialog");
    for (const profile of profiles) await dialog.getByLabel(profile.name, { exact: false }).check();
    await dialog.getByRole("combobox", { name: "Orchestrator model" }).selectOption(profiles[0].id);
    await dialog.getByRole("spinbutton", { name: "Maximum rounds" }).fill("2");
    await dialog.getByRole("spinbutton", { name: "Pause between turns (seconds)" }).fill("1");
    await dialog.locator(".debate-policy summary").click();
    await dialog.getByLabel("Participant invocations (answers and votes)", { exact: true }).fill("32");
    await dialog.getByLabel("Additional question-selection guidance", { exact: true }).fill("Ask about reversible rollout evidence.");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await dialog.getByLabel("Additional context-preservation guidance", { exact: true }).scrollIntoViewIfNeeded();
      await expect(dialog.getByLabel("Additional context-preservation guidance", { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1 && document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-policy-${width}-${theme}.png`, fullPage: true });
    }
    await dialog.getByRole("button", { name: "Start debate", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await page.getByRole("textbox", { name: "Your message" }).fill("Keep a rollback threshold of five percent.");
    await page.getByRole("combobox", { name: "When to apply this context" }).selectOption("next_turn");
    await page.getByRole("button", { name: "Send", exact: true }).click();
    await expect(page.getByRole("textbox", { name: "Your message" })).toHaveValue("");
    const runsPath = `/v2.0/api/conversations/${conversation.id}/runs`;
    await expect.poll(async () => (await (await request.get(runsPath)).json()).at(-1)?.outcome, { timeout: 45000 }).toBe("consensus");
    const run = (await (await request.get(runsPath)).json()).at(-1);
    const proposals = await (await request.get(`${runsPath}/${run.id}/proposals`)).json();
    expect(run.policy.maxParticipantTurns).toBe(32);
    expect(run.policy.questionInstructions).toBe("Ask about reversible rollout evidence.");
    expect(run.usage.participantTurns).toBeGreaterThanOrEqual(8);
    expect(run.usage.orchestrationCalls).toBeGreaterThanOrEqual(2);
    expect(proposals[0].agreements).toHaveLength(4);
    expect(proposals[0].agreements.every(vote => vote.position === "agree")).toBeTruthy();
    const receipts = await (await request.get(`/v2.0/api/conversations/${conversation.id}/interjections`)).json();
    expect(receipts[0].deliveredParticipantIds).toHaveLength(4);
    await page.reload();
    await page.locator(".debate-panel details > summary").filter({ hasText: "Proposals" }).click();
    await expect(page.locator(".agreement-list li")).toHaveCount(4);
    await expect(page.locator(".proposal").first()).toContainText("reversible rollout");
    const history = await (await request.get("/v2.0/api/request-history?pageSize=100")).json();
    const capture = history.items.find(item => item.path === "/v2.0/api/configuration/credentials" && item.method === "POST");
    if (capture) {
      const detail = await (await request.get(`/v2.0/api/request-history/${capture.id}`)).json();
      expect(JSON.stringify(detail)).not.toContain("fictional-gateway-token");
    }
    await request.delete(`/v2.0/api/configuration/credentials/${credential.id}?revision=${credential.revision}`);
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { localStorage.setItem("arguebot.theme", theme); document.documentElement.dataset.theme = theme; }, theme);
      await page.screenshot({ path: `../.run/screenshots/v2-panel-${width}-${theme}.png`, fullPage: true });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1 && document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
    }
  } finally { await fixture.stop(); }
});

test("summary provenance retains structured memory, original hashes and navigable parents", async ({ page, request }) => {
  test.setTimeout(60000);
  const fixture = await startProviderFixture();
  try {
    const profiles = [];
    for (const name of ["Memory A", "Memory B", "Memory moderator"])
      profiles.push(await post(request, "/v2.0/api/participants", { name, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent" }));
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Inspect source-grounded working memory" });
    const prefix = `/v2.0/api/conversations/${conversation.id}`;
    const pinned = await post(request, prefix + "/interjections", { text: "Pinned constraint: keep the canary below five percent.", deliveryMode: "next_turn", pinned: true, idempotencyKey: "memory-pinned" });
    for (let index = 0; index < 20; index++) await post(request, prefix + "/messages", { action: "note", text: `Original evidence ${index}: ` + "Retain uncertainty. ".repeat(90), idempotencyKey: "memory-source-" + index });
    const run = await post(request, prefix + "/runs", { protocolVersion: 2, participantIds: profiles.slice(0, 2).map(profile => profile.id), orchestratorId: profiles[2].id, maxRounds: 1, interventionSeconds: 0, policy: { maxContextCharacters: 16384 }, idempotencyKey: "memory-review" });
    await expect.poll(async () => (await (await request.get(prefix + "/runs/" + run.id)).json()).outcome, { timeout: 20000 }).toBe("consensus");
    const summaries = await (await request.get(prefix + "/runs/" + run.id + "/summaries")).json();
    const latest = summaries.find(summary => summary.active);
    expect(latest.sections).toHaveLength(7);
    expect(latest.parentSummaryIds).toHaveLength(1);
    const source = latest.sources.find(item => item.messageId !== pinned.messageId);
    expect(source.digest).toMatch(/^[a-f0-9]{64}$/);
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    const memory = page.getByRole("region", { name: "Context summaries and sources" });
    await memory.locator("details > summary").filter({ hasText: /^Context summaries and sources$/ }).click();
    const card = memory.locator("article").filter({ has: page.locator(".badge", { hasText: /^Used for context$/ }) });
    await expect(card).toContainText("Contested claims");
    expect(summaries.flatMap(summary => summary.sections).some(section => section.kind === "constraints" && section.content.includes(pinned.messageId)), "A summary covering the pinned constraint must cite its source ID.").toBeTruthy();
    const citation = memory.locator(".markdown").getByRole("button", { name: pinned.messageId, exact: true }).first();
    await expect(citation).toBeVisible();
    await citation.click();
    await expect(page.getByRole("dialog")).toContainText("keep the canary below five percent");
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    await card.locator("summary").filter({ hasText: /^Summary provenance$/ }).click();
    await card.getByRole("button", { name: latest.parentSummaryIds[0], exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Summary provenance" })).toContainText("Original sources");
    await page.getByRole("dialog").getByRole("button", { name: "Close", exact: true }).last().click();
    await card.locator("summary").filter({ hasText: /^Original sources/ }).click();
    await card.getByRole("button", { name: source.messageId, exact: true }).click();
    await expect(page.getByRole("dialog")).toContainText(source.digest);
    await expect(page.getByRole("dialog")).toContainText("Original evidence");
    for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-summary-source-${width}-${theme}.png`, fullPage: true });
    }
  } finally { await fixture.stop(); }
});

test("post-implementation review covers every web route and new configuration forms", async ({ page }) => {
  test.setTimeout(90000);
  for (const width of [390, 768, 1280]) for (const theme of ["light", "dark"]) {
    await page.setViewportSize({ width, height: 900 });
    await page.addInitScript(theme => localStorage.setItem("arguebot.theme", theme), theme);
    for (const route of ["home", "conversations", "participants", "settings", "history", "explorer"]) {
      await page.goto("/dashboard/" + route);
      await expect(page.locator("main")).toBeVisible();
      if (route === "history") await expect(page.locator(".activity-chart")).toBeVisible();
      if (route === "conversations" || route === "participants") await expect(page.locator("table tbody tr").first()).toBeVisible();
      await expect(page.locator("main")).not.toContainText("Unexpected Application Error");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1), route).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-${route || "home"}-${width}-${theme}.png`, fullPage: true });
    }
    await page.goto("/dashboard/participants");
    const tabNames = { endpoints: ["HTTP Endpoints", "Add endpoint"], harnesses: ["CLI Harnesses", "Add harness"], models: ["Models", "Add model"], credentials: ["Credentials", "Add credential"] };
    for (const kind of ["endpoints", "harnesses", "models", "credentials"]) {
      await page.getByRole("tab", { name: new RegExp("^" + tabNames[kind][0]) }).click();
      const toolbar = page.locator(".tab-toolbar");
      const [left, right] = await Promise.all([toolbar.locator("p").boundingBox(), toolbar.getByRole("button", { name: tabNames[kind][1], exact: true }).boundingBox()]);
      if (width > 767) expect(Math.abs((left.y + left.height / 2) - (right.y + right.height / 2))).toBeLessThan(24);
      const bar = await toolbar.boundingBox();
      expect(Math.abs((bar.x + bar.width) - (right.x + right.width))).toBeLessThan(2);
      await page.screenshot({ path: `../.run/screenshots/v2-${kind}-tab-${width}-${theme}.png`, fullPage: true });
      await page.getByRole("tabpanel").getByRole("button", { name: tabNames[kind][1], exact: true }).click();
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      if (kind === "harnesses") {
        await expect(dialog).toContainText("uses the login of the user running ArgueBot");
        await expect(dialog.getByRole("combobox", { name: "Stored credential", exact: true }).locator("option")).toHaveCount(1);
        await dialog.getByRole("combobox", { name: "CLI harness", exact: true }).selectOption("mux");
        await expect(dialog.getByRole("combobox", { name: "Stored credential", exact: true })).toHaveCount(0);
        await expect(dialog).toContainText("has no stored credential");
        await dialog.getByRole("combobox", { name: "CLI harness", exact: true }).selectOption("claude");
        await dialog.getByRole("textbox", { name: "Inherited environment names", exact: true }).fill("GEMINI_API_KEY, FIXTURE_ROUTE");
        await expect(dialog).toContainText("never values");
        await dialog.getByRole("textbox", { name: "Inherited environment names", exact: true }).scrollIntoViewIfNeeded();
        await page.screenshot({ path: `../.run/screenshots/v2-harness-environment-${width}-${theme}.png`, fullPage: true });
      }
      await dialog.getByRole("button", { name: "Save", exact: true }).scrollIntoViewIfNeeded();
      await expect(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-${kind}-form-${width}-${theme}.png`, fullPage: true });
      await page.keyboard.press("Escape");
      await expect(dialog).toHaveCount(0);
    }
  }
});

test("setup errors name their field and a moderator revision restarts voting", async ({ page, request }) => {
  test.setTimeout(90000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  try {
    const create = name => post(request, "/v2.0/api/participants", { name, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent" });
    const first = await create("Twin " + suffix);
    const twin = await create("twin " + suffix);
    const second = await create("Revision B " + suffix);
    const moderator = await create("Revision moderator " + suffix);
    const doomed = await create("Removed seat " + suffix);
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Moderator revision " + suffix });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    await page.getByRole("button", { name: "Start another debate", exact: true }).click();
    const dialog = page.getByRole("dialog");
    const seat = name => dialog.locator("label.participant-choice").filter({ hasText: new RegExp("^" + name) }).getByRole("checkbox");
    await seat(first.name).check();
    await seat(twin.name).check();
    await expect(dialog.getByRole("alert")).toContainText("share the name");
    await expect(dialog.getByRole("button", { name: "Start debate", exact: true })).toBeDisabled();
    await seat(twin.name).uncheck();
    await seat(doomed.name).check();
    await dialog.getByRole("combobox", { name: "Orchestrator model" }).selectOption(moderator.id);
    expect((await request.delete(`/v2.0/api/participants/${doomed.id}`)).ok()).toBeTruthy();
    await dialog.getByRole("button", { name: "Start debate", exact: true }).click();
    await expect(dialog.locator(".field-error").last()).toContainText("was deleted");
    await expect(dialog.getByRole("status")).toContainText("removed from this panel");
    await expect(dialog.locator(".selected-panel li")).toHaveCount(1);
    await expect(dialog.getByRole("button", { name: "Start debate", exact: true })).toBeDisabled();
    for (const width of [390, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await dialog.locator(".field-error").last().scrollIntoViewIfNeeded();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-setup-error-${width}-${theme}.png`, fullPage: true });
    }
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    const prefix = `/v2.0/api/conversations/${conversation.id}/runs`;
    const run = await post(request, prefix, { protocolVersion: 2, participantIds: [first.id, second.id], orchestratorId: moderator.id, maxRounds: 1, interventionSeconds: 600, idempotencyKey: "revision-" + suffix });
    let current;
    for (let step = 0; step < 40; step++) {
      current = await (await request.get(`${prefix}/${run.id}`)).json();
      if (current.phase === "ratification" && current.state === "window") break;
      if (current.state === "window") await request.post(`${prefix}/${run.id}/actions`, { data: { action: "continue", idempotencyKey: `continue-${suffix}-${step}` } });
      await page.waitForTimeout(150);
    }
    expect(current.phase).toBe("ratification");
    await page.reload();
    await page.locator(".debate-panel details.proposals > summary").click();
    await page.getByRole("button", { name: "Edit proposal", exact: true }).click();
    const editor = page.getByRole("dialog", { name: "Edit proposal" });
    await expect(editor.getByRole("button", { name: "Save and restart voting", exact: true })).toBeDisabled();
    await editor.getByRole("textbox", { name: "Revised proposal" }).fill("Adopt the reversible design behind a five percent canary.");
    for (const width of [390, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.screenshot({ path: `../.run/screenshots/v2-edit-proposal-${width}-${theme}.png`, fullPage: true });
    }
    await editor.getByRole("button", { name: "Save and restart voting", exact: true }).click();
    await expect(editor).toHaveCount(0);
    await expect(page.locator(".proposal .badge", { hasText: "Your revision" })).toHaveCount(1);
    await expect(page.locator(".proposal .badge", { hasText: /^Replaced$/ })).toHaveCount(1);
    for (let step = 0; step < 40; step++) {
      current = await (await request.get(`${prefix}/${run.id}`)).json();
      if (current.state === "completed" || current.state === "paused") break;
      if (current.state === "window") await request.post(`${prefix}/${run.id}/actions`, { data: { action: "continue", idempotencyKey: `finish-${suffix}-${step}` } });
      await page.waitForTimeout(150);
    }
    expect(current.outcome).toBe("consensus");
    const proposals = await (await request.get(`${prefix}/${run.id}/proposals`)).json();
    const revision = proposals.find(proposal => proposal.moderatorEdited);
    expect(revision.content).toContain("five percent canary");
    expect(revision.agreements.filter(vote => vote.position === "agree" && !vote.superseded)).toHaveLength(2);
    expect(proposals.find(proposal => proposal.id === revision.supersedesId).superseded).toBeTruthy();
    await page.reload();
    await expect(page.locator(".debate-panel details.proposals")).toHaveAttribute("open", "");
    await expect(page.getByRole("button", { name: "Edit proposal", exact: true })).toHaveCount(0);
    for (const width of [390, 1280]) for (const theme of ["light", "dark"]) {
      await page.setViewportSize({ width, height: 900 });
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBeTruthy();
      await page.locator(".proposal").first().scrollIntoViewIfNeeded();
      await page.screenshot({ path: `../.run/screenshots/v2-revised-outcome-${width}-${theme}.png` });
    }
  } finally { await fixture.stop(); }
});
