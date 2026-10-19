import { test, expect } from "@playwright/test";
import fs from "node:fs/promises";
import path from "node:path";
async function code() {
  return (
    await fs.readFile(
      path.join(process.env.ARGUEBOT_E2E_HOME, "runtime/pairing-code"),
      "utf8",
    )
  ).trim();
}
async function enroll(page) {
  await page.goto("/");
  await page.getByLabel("Pairing code", { exact: true }).fill(await code());
  await page.getByRole("button", { name: "Connect securely" }).click();
  await expect(
    page.getByRole("heading", { name: "Your thinking, in progress" }),
  ).toBeVisible();
}
test("pair, create, save note, export and restore theme", async ({ page }) => {
  const failures = [];
  page.on("pageerror", (error) => failures.push(error.message));
  await enroll(page);
  await page
    .getByRole("button", { name: "New conversation", exact: true })
    .first()
    .click();
  const title = "Should we isolate billing? " + Date.now();
  await page.getByRole("textbox", { name: "Question or title" }).fill(title);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("heading", { name: title })).toBeVisible();
  await page
    .getByRole("textbox", { name: "Your message" })
    .fill("Consider rollback and ownership boundaries.");
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await expect(
    page.getByText("Consider rollback and ownership boundaries.", {
      exact: true,
    }),
  ).toBeVisible();
  const download = page.getByRole("button", { name: "Download transcript" });
  await expect(download).toHaveText("");
  await expect(download).toHaveAttribute("title", /Markdown/);
  const [file] = await Promise.all([
    page.waitForEvent("download"),
    download.click(),
  ]);
  expect(file.suggestedFilename()).toMatch(/\.md$/);
  const theme = await page.locator("html").getAttribute("data-theme");
  await page
    .getByRole("button", { name: /Switch to (light|dark) mode/ })
    .click();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute(
    "data-theme",
    theme === "dark" ? "light" : "dark",
  );
  await expect(
    page.getByText("Consider rollback and ownership boundaries.", {
      exact: true,
    }),
  ).toBeVisible();
  expect(failures).toEqual([]);
});
for (const width of [1280, 768, 390]) {
  for (const theme of ["light", "dark"]) {
    test(`responsive conversation and forms ${width} ${theme}`, async ({
      page,
      request,
    }) => {
      const paired = await request.post("/v1.0/api/auth/pair", {
        data: { code: await code() },
      });
      expect(paired.ok()).toBeTruthy();
      const { token } = await paired.json();
      const created = await request.post("/v1.0/api/conversations", {
        headers: { Authorization: `Bearer ${token}` },
        data: {
          title: "A durable conversation about the next architecture decision",
        },
      });
      expect(created.ok()).toBeTruthy();
      const conversation = await created.json();
      await page.addInitScript(
        ({ token, theme }) => {
          sessionStorage.setItem("arguebot.session", token);
          localStorage.setItem("arguebot.theme", theme);
        },
        { token, theme },
      );
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/dashboard/conversations/" + conversation.id);
      await expect(
        page.getByRole("heading", { name: conversation.title }),
      ).toBeVisible();
      await page.screenshot({
        path: `../.run/screenshots/workspace-${width}-${theme}.png`,
        fullPage: true,
      });
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      ).toBeTruthy();
      const controls = await page
        .locator("button,input,textarea,select,a")
        .evaluateAll((nodes) =>
          nodes
            .filter(
              (node) =>
                node.getClientRects().length &&
                !node.title &&
                !node.closest("[title]"),
            )
            .map((node) => node.outerHTML),
        );
      expect(controls).toEqual([]);
      await page.goto("/dashboard/participants");
      await page
        .getByRole("button", { name: "Add participant", exact: true })
        .click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await page.screenshot({
        path: `../.run/screenshots/participant-form-${width}-${theme}.png`,
        fullPage: true,
      });
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      ).toBeTruthy();
    });
  }
}
test("inspect redacted request history and execute a read-only API request", async ({
  page,
}) => {
  await enroll(page);
  await page.goto("/dashboard/history");
  await expect(
    page.getByRole("heading", { name: "Request history", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Inspect request" }).first(),
  ).toBeVisible();
  await page.getByRole("button", { name: "Inspect request" }).first().click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(
    page.getByRole("dialog").getByText("Request ID", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Close", exact: true })
    .first()
    .click();
  await page.goto("/dashboard/explorer");
  await expect(
    page.getByRole("heading", { name: "API Explorer", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "GET /health", exact: true }).click();
  await page
    .getByRole("button", { name: "Execute request", exact: true })
    .click();
  await expect(page.locator(".explorer-response")).toContainText("200");
  await expect(page.locator(".explorer-response pre")).toContainText(
    '"status":"ready"',
  );
});
test("participant argument editor preserves blank literal arguments", async ({
  page,
}) => {
  await enroll(page);
  await page.goto("/dashboard/participants");
  await page
    .getByRole("button", { name: "Add participant", exact: true })
    .click();
  await page.getByRole("button", { name: "Add argument", exact: true }).click();
  await page
    .getByRole("textbox", { name: "Argument 1", exact: true })
    .fill("--first");
  await page.getByRole("button", { name: "Add argument", exact: true }).click();
  await expect(
    page.getByRole("textbox", { name: "Argument 2", exact: true }),
  ).toHaveValue("");
  await page.getByRole("button", { name: "Add argument", exact: true }).click();
  await page
    .getByRole("textbox", { name: "Argument 3", exact: true })
    .fill("two words");
  await expect(
    page.getByRole("textbox", { name: "Argument 1", exact: true }),
  ).toHaveValue("--first");
  await expect(
    page.getByRole("textbox", { name: "Argument 2", exact: true }),
  ).toHaveValue("");
});

test("create, verify, download and delete a recovery archive", async ({
  page,
}) => {
  await enroll(page);
  await page.getByRole("link", { name: "Backups", exact: true }).click();
  await page
    .getByRole("button", { name: "Create backup", exact: true })
    .click();
  const archiveName = "Browser recovery fixture " + Date.now();
  await page
    .getByRole("textbox", { name: "Name", exact: true })
    .fill(archiveName);
  await page
    .getByLabel("Passphrase", { exact: true })
    .fill("browser-fixture-passphrase");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Create backup", exact: true })
    .click();
  await expect(page.getByText("create: completed")).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: archiveName });
  await expect(row).toContainText("ready");
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    row.getByRole("button", { name: "Download backup" }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(/\.arguebak$/);
  await row.getByRole("button", { name: "Verify", exact: true }).click();
  await page
    .getByLabel("Passphrase", { exact: true })
    .fill("browser-fixture-passphrase");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Verify", exact: true })
    .click();
  await expect(page.getByText("verify: completed")).toBeVisible();
  await row.getByRole("button", { name: "Restore", exact: true }).click();
  await page
    .getByLabel("Passphrase", { exact: true })
    .fill("browser-fixture-passphrase");
  await page
    .getByLabel("New home name", { exact: true })
    .fill("browser-pg-target");
  await page
    .getByLabel("Destination database", { exact: true })
    .selectOption("Postgresql");
  const missingReference = "ARGUEBOT_TEST_MISSING_" + Date.now();
  await page
    .getByLabel("PostgreSQL connection environment reference", { exact: true })
    .fill(missingReference);
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Restore", exact: true })
    .click();
  await expect(
    page
      .getByRole("dialog")
      .getByText(/Set a PostgreSQL connection environment reference/),
  ).toBeVisible();
  await expect(
    page.getByLabel("PostgreSQL connection environment reference", {
      exact: true,
    }),
  ).toHaveValue(missingReference);
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Cancel", exact: true })
    .click();
  const archiveId = await row.locator("td").first().getAttribute("title");
  await page.reload();
  const jobs = page
    .locator("section")
    .filter({
      has: page.getByRole("heading", { name: "Recovery jobs", exact: true }),
    });
  const verified = jobs
    .getByRole("row")
    .filter({ hasText: archiveId })
    .filter({ hasText: "verify" });
  await expect(verified).toContainText("completed");
  await verified.getByRole("button").click();
  await expect(page.getByText("verify: completed")).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  for (const theme of ["light", "dark"]) {
    if ((await page.locator("html").getAttribute("data-theme")) !== theme)
      await page
        .getByRole("button", { name: /Switch to (light|dark) mode/ })
        .click();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth + 1,
      ),
    ).toBeTruthy();
    await page.screenshot({
      path: path.join("../.run/screenshots", `recovery-390-${theme}.png`),
      fullPage: true,
    });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await row.getByRole("button", { name: "Delete", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Delete", exact: true })
    .click();
  await expect(row).toHaveCount(0);
});

test("inspect immutable security accounting and filter empty results", async ({ page }) => {
  await enroll(page);
  expect((await page.request.get("/v1.0/api/auth/me")).status()).toBe(401);
  await page.getByRole("link", { name: "Security audit", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Security audit", exact: true })).toBeVisible();
  await page.getByRole("searchbox", { name: "Operation code" }).fill("http.authorization");
  await expect(page.getByRole("cell", { name: "http.authorization", exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Inspect security event", exact: true }).first().click();
  await expect(page.getByRole("dialog")).toContainText("Audit events cannot be edited or deleted");
  await expect(page.getByRole("dialog")).toContainText("Safe error code");
  await page.keyboard.press("Escape");
  for (const theme of ["light", "dark"]) {
    await page.setViewportSize({ width: 390, height: 844 });
    const current = await page.locator("html").getAttribute("data-theme");
    if (current !== theme) await page.getByRole("button", { name: /Switch to .* mode/ }).click();
    await page.screenshot({ path: path.join(process.env.ARGUEBOT_E2E_SCREENSHOTS || "../.run/screenshots", `audit-390-${theme}.png`), fullPage: true });
  }
  await page.getByRole("searchbox", { name: "Operation code" }).fill("no-such-security-operation");
  await expect(page.getByText("No security events match this operation.")).toBeVisible();
});

test("run a real PolyPrompt fixture debate and inspect durable attempt context", async ({ page }) => {
  const { startProviderFixture } = await import("./provider-fixture.js");
  const fixture = await startProviderFixture();
  try {
    await enroll(page);
    const profiles = await page.evaluate(async (endpoint) => {
      const result = [];
      for (const name of ["Attempt browser A " + crypto.randomUUID(), "Attempt browser B " + crypto.randomUUID()]) {
        const response = await fetch("/v1.0/api/participants", { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("arguebot.session")}`, "Content-Type": "application/json" }, body: JSON.stringify({ name, kind: "rest", provider: "ollama", endpoint, model: "fixture", timeoutSeconds: 5 }) });
        if (!response.ok) throw new Error("Provider fixture profile setup failed.");
        result.push(await response.json());
      }
      return result;
    }, fixture.endpoint);
    await page.getByRole("button", { name: "New conversation", exact: true }).first().click();
    await page.getByRole("textbox", { name: "Question or title" }).fill("Attempt browser immutable question");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await page.getByRole("textbox", { name: "Your message" }).fill("Use this exact source note for the fixture debate.");
    await page.getByRole("button", { name: "Send", exact: true }).click();
    await expect(page.getByText("Use this exact source note for the fixture debate.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Start another debate", exact: true }).click();
    const start = page.getByRole("dialog");
    for (const profile of profiles) await start.getByLabel(profile.name, { exact: false }).check();
    await start.getByRole("spinbutton", { name: "Maximum rounds" }).fill("1");
    await start.getByRole("spinbutton", { name: "Pause between turns (seconds)" }).fill("0");
    await start.getByRole("button", { name: "Start debate", exact: true }).click();
    await expect(page.getByText("Fixture answer.", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Generation attempts", exact: true }).click();
    const inspector = page.getByRole("dialog");
    await expect(inspector.getByRole("button", { name: "Inspect attempt", exact: true })).toHaveCount(2);
    await inspector.getByRole("button", { name: "Inspect attempt", exact: true }).first().click();
    await expect(inspector).toContainText("debate-v1");
    await expect(inspector).toContainText("Use this exact source note for the fixture debate.");
    await expect(inspector).toContainText("Attempt browser immutable question");
    await page.setViewportSize({ width: 390, height: 844 });
    await inspector.locator(".inspector-section").scrollIntoViewIfNeeded();
    await page.screenshot({ path: "../.run/screenshots/attempt-context-390-light.png", fullPage: true });
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "Switch to dark mode", exact: true }).click();
    await page.getByRole("button", { name: "Generation attempts", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Inspect attempt", exact: true }).first().click();
    await page.getByRole("dialog").locator(".inspector-section").scrollIntoViewIfNeeded();
    await page.screenshot({ path: "../.run/screenshots/attempt-context-390-dark.png", fullPage: true });
    await page.keyboard.press("Escape");
    await page.reload();
    await page.getByRole("button", { name: "Generation attempts", exact: true }).click();
    await expect(page.getByRole("dialog").getByRole("button", { name: "Inspect attempt", exact: true })).toHaveCount(2);
  } finally { await fixture.stop(); }
});

test("page long history, read complete text and export beyond the visible page", async ({ page }) => {
  await enroll(page);
  await page.getByRole("button", { name: "New conversation", exact: true }).first().click();
  await page.getByRole("textbox", { name: "Question or title" }).fill("Paged browser history fixture");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Paged browser history fixture" })).toBeVisible();
  await page.evaluate(async () => {
    const id = location.pathname.split("/").at(-1);
    for (let index = 0; index < 35; index++) {
      const text = index === 0 ? "LONG SOURCE START " + "議論🙂 ".repeat(3000) + "\nCOMPLETE BROWSER TRANSCRIPT TAIL" : "Paged browser message " + index;
      const response = await fetch(`/v1.0/api/conversations/${id}/messages`, { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("arguebot.session")}`, "Content-Type": "application/json" }, body: JSON.stringify({ action: "note", text, idempotencyKey: `page-fixture-${index}` }) });
      if (!response.ok) throw new Error("History fixture setup failed.");
    }
  });
  await page.reload();
  await expect(page.getByText("Paged browser message 34", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Previous", exact: true }).click();
  const read = page.getByRole("button", { name: "Read full message", exact: true });
  await expect(read).toBeVisible();
  await expect(page.getByText("COMPLETE BROWSER TRANSCRIPT TAIL", { exact: true })).toHaveCount(0);
  await read.click();
  await expect(page.getByRole("dialog")).toContainText("COMPLETE BROWSER TRANSCRIPT TAIL");
  await page.keyboard.press("Escape");
  await page.evaluate(async () => {
    const id = location.pathname.split("/").at(-1);
    const response = await fetch(`/v1.0/api/conversations/${id}/messages`, { method: "POST", headers: { Authorization: `Bearer ${sessionStorage.getItem("arguebot.session")}`, "Content-Type": "application/json" }, body: JSON.stringify({ action: "note", text: "New message beyond captured membership", idempotencyKey: "page-fixture-newer" }) });
    if (!response.ok) throw new Error("Concurrent history append failed.");
  });
  await expect(page.getByRole("button", { name: "Newer messages available", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Read full message", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Newer messages available", exact: true }).click();
  await expect(page.getByText("New message beyond captured membership", { exact: true })).toBeVisible();
  const downloaded = page.waitForEvent("download");
  await page.getByRole("button", { name: "Download transcript", exact: true }).click();
  const artifact = await downloaded;
  const text = await fs.readFile(await artifact.path(), "utf8");
  expect(text).toContain("COMPLETE BROWSER TRANSCRIPT TAIL");
  expect(text).toContain("New message beyond captured membership");
  await page.getByRole("combobox", { name: "Records per page", exact: true }).selectOption("10");
  await expect(page.getByRole("button", { name: "Read full message", exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "../.run/screenshots/message-pages-390.png", fullPage: true });
});
