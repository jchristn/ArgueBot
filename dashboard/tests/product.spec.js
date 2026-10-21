import { test, expect } from "@playwright/test";
import { spawnSync } from "node:child_process";
import fs from "node:fs/promises";
import net from "node:net";
import path from "node:path";
async function openDashboard(page) {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Your thinking, in progress" })).toBeVisible();
}
test("REST participant executes local tools and preserves visible tool history", async ({ page }) => {
  test.skip(!process.env.ARGUEBOT_SIMULATION_URL, "Start the committed provider simulation and set ARGUEBOT_SIMULATION_URL.");
  const home = await fs.mkdtemp(path.resolve("../.run/rest tool browser "));
  const workspace = path.join(home, "workspace"); await fs.mkdir(workspace);
  const listener = net.createServer();
  await new Promise((resolve) => listener.listen(0, "127.0.0.1", resolve));
  const port = listener.address().port; await new Promise((resolve) => listener.close(resolve));
  const cli = path.resolve("../src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll");
  const invoke = (...args) => spawnSync("dotnet", [cli, "--home", home, ...args], { encoding: "utf8", timeout: 10000, env: { ...process.env, ARGUEBOT_TOKEN: "" } });
  const origin = `http://127.0.0.1:${port}`;
  try {
    expect(invoke("service", "web", "--port", String(port)).status).toBe(0);
    await page.goto(origin + "/dashboard/participants");
    await page.getByRole("button", { name: "Add participant", exact: true }).click();
    await page.getByLabel("Runner type", { exact: true }).selectOption("rest");
    await page.getByLabel("Display name", { exact: true }).fill("REST tool fixture");
    await page.getByLabel("Endpoint URL", { exact: true }).fill(process.env.ARGUEBOT_SIMULATION_URL);
    await page.getByLabel("Model identifier", { exact: true }).fill("local-tools");
    await page.getByLabel("Working directory", { exact: true }).fill(workspace);
    await expect(page.getByText("REST participants can read and write local files", { exact: false })).toBeVisible();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByRole("dialog")).not.toBeVisible();
    const profiles = await (await page.request.get(origin + "/v1.0/api/participants")).json();
    const profile = profiles.find((item) => item.name === "REST tool fixture"); expect(profile.workingDirectory).toBe(workspace);
    const conversation = await (await page.request.post(origin + "/v1.0/api/conversations", { data: { title: "REST tool browser evidence" } })).json();
    const question = JSON.stringify({ fixtureExecutable: path.resolve("../src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll") });
    const run = await page.request.post(origin + `/v1.0/api/conversations/${conversation.id}/runs`, { data: { mode: "followup", question, participantIds: [profile.id], maxRounds: 1, idempotencyKey: "browser-tools" } });
    expect(run.status()).toBe(202);
    await page.goto(origin + `/dashboard/conversations/${conversation.id}`);
    await expect(page.getByText("Local tools verified.", { exact: true })).toBeVisible();
    await expect(page.getByText("Local tool", { exact: true })).toHaveCount(8);
    await expect(page.locator("article.message").filter({ hasText: "Status: completed" })).toHaveCount(4);
    expect(await fs.readFile(path.join(workspace, "tool fixture.txt"), "utf8")).toBe("Local tool evidence 議論");
    await page.reload();
    await expect(page.getByText("Local tool", { exact: true })).toHaveCount(8);
    await page.screenshot({ path: "../.run/screenshots/rest-local-tools.png", fullPage: true });
  } finally { invoke("service", "stop"); await fs.rm(home, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 }); }
});

test("plain local dashboard opens and writes without authentication or pairing", async ({ page }) => {
  const home = await fs.mkdtemp(path.resolve("../.run/open local workspace "));
  const listener = net.createServer();
  await new Promise((resolve) => listener.listen(0, "127.0.0.1", resolve));
  const port = listener.address().port;
  await new Promise((resolve) => listener.close(resolve));
  const cli = path.resolve("../src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll");
  const invoke = (...args) => spawnSync("dotnet", [cli, "--home", home, ...args], { encoding: "utf8", timeout: 10000, env: { ...process.env, ARGUEBOT_TOKEN: "" } });
  try {
    const launch = invoke("service", "web", "--port", String(port));
    expect(launch.status).toBe(0);
    const url = `http://127.0.0.1:${port}/dashboard`;
    expect(launch.stdout).toContain(url);
    expect(launch.stdout).not.toMatch(/#|connect=|pairing/i);
    expect(await fs.access(path.join(home, "runtime/pairing-code")).then(() => true, () => false)).toBe(false);
    const requests = [];
    page.on("request", (request) => requests.push({ url: request.url(), authorization: request.headers().authorization }));
    await page.goto(url);
    await expect(page.getByRole("heading", { name: "Your thinking, in progress" })).toBeVisible();
    await expect(page.getByText("Local workspace", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out", exact: true })).not.toBeVisible();
    for (const [provider, flag] of [["claude", "--dangerously-skip-permissions"], ["codex", "--yolo"], ["mux", "--no-session-persistence"]]) {
      const created = await page.request.post(`http://127.0.0.1:${port}/v1.0/api/participants`, { data: { kind: "cli", provider } });
      expect(created.status()).toBe(201);
      const profile = await created.json();
      expect(profile.executable).toBe(provider);
      expect(profile.arguments).toContain(flag);
      const read = await page.request.get(`http://127.0.0.1:${port}/v1.0/api/participants`);
      expect((await read.json()).find((item) => item.id === profile.id).arguments).toEqual(profile.arguments);
      const custom = await page.request.put(`http://127.0.0.1:${port}/v1.0/api/participants/${profile.id}`, { data: { ...profile, arguments: ["--custom", "", "two words"] } });
      expect(custom.ok()).toBe(true);
      expect((await custom.json()).arguments).toEqual(["--custom", "", "two words"]);
      const restored = await page.request.put(`http://127.0.0.1:${port}/v1.0/api/participants/${profile.id}`, { data: { ...profile, arguments: [], executable: "" } });
      expect(restored.ok()).toBe(true);
      expect((await restored.json()).arguments).toEqual(profile.arguments);
    }
    const cliProfile = invoke("participants", "add", JSON.stringify({ kind: "cli", provider: "codex" }));
    expect(cliProfile.status).toBe(0);
    expect(JSON.parse(cliProfile.stdout).arguments).toContain("--yolo");
    await page.goto(url + "/participants");
    await page.getByRole("button", { name: "Add participant", exact: true }).click();
    const runner = page.getByLabel("Runner type", { exact: true });
    await expect(runner).toHaveValue("claude");
    await expect(page.getByRole("textbox", { name: "Argument 3", exact: true })).toHaveValue("--dangerously-skip-permissions");
    await runner.selectOption("codex");
    await expect(page.getByRole("textbox", { name: "Executable (program only)", exact: true })).toHaveValue("codex");
    await expect(page.getByRole("textbox", { name: "Argument 2", exact: true })).toHaveValue("--yolo");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByRole("dialog")).not.toBeVisible();
    await page.goto(url);
    const result = await page.evaluate(async () => {
      const health = await (await fetch("/health")).json();
      const saved = await fetch("/v1.0/api/conversations", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title: "Open local workspace fixture" }) });
      const conversation = await saved.json();
      const controller = new AbortController();
      const events = await fetch("/v1.0/api/conversations/" + conversation.id + "/events", { headers: { Accept: "text/event-stream" }, signal: controller.signal });
      const reader = events.body.getReader();
      const first = await reader.read();
      const eventText = new TextDecoder().decode(first.value);
      controller.abort();
      return { health, saved: saved.status, eventText };
    });
    expect(result.health.authenticationRequired).toBe(false);
    expect(result.saved).toBe(201);
    expect(result.eventText).not.toContain("session_revoked");
    expect(result.eventText).toContain("event:");
    await page.reload();
    await expect(page.getByRole("heading", { name: "Your thinking, in progress" })).toBeVisible();
    await page.goto(url + "#connect=obsolete-link");
    await expect(page.getByRole("heading", { name: "Your thinking, in progress" })).toBeVisible();
    await expect(page).not.toHaveURL(/#/);
    expect(requests.every((request) => !request.authorization && !request.url.endsWith("/auth/pair"))).toBe(true);
    for (const width of [390, 1280]) {
      await page.setViewportSize({ width, height: 900 });
      await page.screenshot({ path: `../.run/screenshots/open-local-${width}.png`, fullPage: true });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
  } finally {
    invoke("service", "stop");
    for (let retry = 0; retry < 50; retry++) {
      if (!(await fs.access(path.join(home, "runtime/service.json")).then(() => true, () => false))) break;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    await fs.rm(home, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
  }
});
test("open, create, save note, export and restore theme", async ({ page }) => {
  const failures = [];
  page.on("pageerror", (error) => failures.push(error.message));
  await openDashboard(page);
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
      const created = await request.post("/v1.0/api/conversations", {
        data: {
          title: "A durable conversation about the next architecture decision",
        },
      });
      expect(created.ok()).toBeTruthy();
      const conversation = await created.json();
      await page.addInitScript(theme => localStorage.setItem("arguebot.theme", theme), theme);
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
  await openDashboard(page);
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
  await page.getByRole("combobox", { name: "Operation", exact: true }).selectOption("get /health");
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
  await openDashboard(page);
  await page.goto("/dashboard/participants");
  await page
    .getByRole("button", { name: "Add participant", exact: true })
    .click();
  const executable = page.getByRole("textbox", { name: "Executable (program only)", exact: true });
  await executable.fill("claude --dangerously-skip-permissions");
  await expect(page.getByRole("alert")).toContainText("Put each flag or value in a separate argument row");
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
  await expect(page.getByText("Selecting Claude Code, Codex", { exact: false })).toBeVisible();
  await page.getByLabel("Runner type", { exact: true }).selectOption("plain");
  await page.screenshot({ path: "../.run/screenshots/participant-command-guidance.png" });
  await executable.fill("/fixture directory/agent");
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeEnabled();
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

test("run a real PolyPrompt fixture debate and inspect durable attempt context", async ({
  page,
}) => {
  const { startProviderFixture } = await import("./provider-fixture.js");
  const fixture = await startProviderFixture();
  try {
    await openDashboard(page);
    const profiles = await page.evaluate(async (endpoint) => {
      const result = [];
      for (const name of [
        "Attempt browser A " + crypto.randomUUID(),
        "Attempt browser B " + crypto.randomUUID(),
      ]) {
        const response = await fetch("/v1.0/api/participants", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            name,
            kind: "rest",
            provider: "ollama",
            endpoint,
            model: "fixture",
            timeoutSeconds: 5,
          }),
        });
        if (!response.ok)
          throw new Error("Provider fixture profile setup failed.");
        result.push(await response.json());
      }
      return result;
    }, fixture.endpoint);
    await page
      .getByRole("button", { name: "New conversation", exact: true })
      .first()
      .click();
    await page
      .getByRole("textbox", { name: "Question or title" })
      .fill("Attempt browser immutable question");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await page
      .getByRole("textbox", { name: "Your message" })
      .fill("Use this exact source note for the fixture debate.");
    await page.getByRole("button", { name: "Send", exact: true }).click();
    await expect(
      page.getByText("Use this exact source note for the fixture debate.", {
        exact: true,
      }),
    ).toBeVisible();
    await page
      .getByRole("button", { name: "Start another debate", exact: true })
      .click();
    const start = page.getByRole("dialog");
    for (const profile of profiles)
      await start.getByLabel(profile.name, { exact: false }).check();
    await start.getByRole("spinbutton", { name: "Maximum rounds" }).fill("1");
    await start
      .getByRole("spinbutton", { name: "Pause between turns (seconds)" })
      .fill("0");
    await start
      .getByRole("button", { name: "Start debate", exact: true })
      .click();
    await expect(
      page.getByText("Fixture answer.", { exact: true }),
    ).toHaveCount(2);
    await page
      .getByRole("button", { name: "Generation attempts", exact: true })
      .click();
    const inspector = page.getByRole("dialog");
    await expect(
      inspector.getByRole("button", { name: "Inspect attempt", exact: true }),
    ).toHaveCount(2);
    await inspector
      .getByRole("button", { name: "Inspect attempt", exact: true })
      .first()
      .click();
    await expect(inspector).toContainText("debate-v1");
    await expect(inspector).toContainText(
      "Use this exact source note for the fixture debate.",
    );
    await expect(inspector).toContainText("Attempt browser immutable question");
    await page.setViewportSize({ width: 390, height: 844 });
    await inspector.locator(".inspector-section").scrollIntoViewIfNeeded();
    await page.screenshot({
      path: "../.run/screenshots/attempt-context-390-light.png",
      fullPage: true,
    });
    await page.keyboard.press("Escape");
    await page
      .getByRole("button", { name: "Switch to dark mode", exact: true })
      .click();
    await page
      .getByRole("button", { name: "Generation attempts", exact: true })
      .click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "Inspect attempt", exact: true })
      .first()
      .click();
    await page
      .getByRole("dialog")
      .locator(".inspector-section")
      .scrollIntoViewIfNeeded();
    await page.screenshot({
      path: "../.run/screenshots/attempt-context-390-dark.png",
      fullPage: true,
    });
    await page.keyboard.press("Escape");
    await page.reload();
    await page
      .getByRole("button", { name: "Generation attempts", exact: true })
      .click();
    await expect(
      page
        .getByRole("dialog")
        .getByRole("button", { name: "Inspect attempt", exact: true }),
    ).toHaveCount(2);
  } finally {
    await fixture.stop();
  }
});

test("page long history, read complete text and export beyond the visible page", async ({
  page,
}) => {
  await openDashboard(page);
  await page
    .getByRole("button", { name: "New conversation", exact: true })
    .first()
    .click();
  await page
    .getByRole("textbox", { name: "Question or title" })
    .fill("Paged browser history fixture");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Paged browser history fixture" }),
  ).toBeVisible();
  await page.evaluate(async () => {
    const id = location.pathname.split("/").at(-1);
    for (let index = 0; index < 35; index++) {
      const text =
        index === 0
          ? "LONG SOURCE START " +
            "議論🙂 ".repeat(3000) +
            "\nCOMPLETE BROWSER TRANSCRIPT TAIL"
          : "Paged browser message " + index;
      const response = await fetch(`/v1.0/api/conversations/${id}/messages`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          action: "note",
          text,
          idempotencyKey: `page-fixture-${index}`,
        }),
      });
      if (!response.ok) throw new Error("History fixture setup failed.");
    }
  });
  await page.reload();
  await expect(
    page.getByText("Paged browser message 34", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Previous", exact: true }).click();
  const read = page.getByRole("button", {
    name: "Read full message",
    exact: true,
  });
  await expect(read).toBeVisible();
  await expect(
    page.getByText("COMPLETE BROWSER TRANSCRIPT TAIL", { exact: true }),
  ).toHaveCount(0);
  await read.click();
  await expect(page.getByRole("dialog")).toContainText(
    "COMPLETE BROWSER TRANSCRIPT TAIL",
  );
  await page.keyboard.press("Escape");
  await page.evaluate(async () => {
    const id = location.pathname.split("/").at(-1);
    const response = await fetch(`/v1.0/api/conversations/${id}/messages`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        action: "note",
        text: "New message beyond captured membership",
        idempotencyKey: "page-fixture-newer",
      }),
    });
    if (!response.ok) throw new Error("Concurrent history append failed.");
  });
  await expect(
    page.getByRole("button", { name: "Newer messages available", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Read full message", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Newer messages available", exact: true })
    .click();
  await expect(
    page.getByText("New message beyond captured membership", { exact: true }),
  ).toBeVisible();
  const downloaded = page.waitForEvent("download");
  await page
    .getByRole("button", { name: "Download transcript", exact: true })
    .click();
  const artifact = await downloaded;
  const text = await fs.readFile(await artifact.path(), "utf8");
  expect(text).toContain("COMPLETE BROWSER TRANSCRIPT TAIL");
  expect(text).toContain("New message beyond captured membership");
  await page
    .getByRole("combobox", { name: "Records per page", exact: true })
    .selectOption("10");
  await expect(
    page.getByRole("button", { name: "Read full message", exact: true }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({
    path: "../.run/screenshots/message-pages-390.png",
    fullPage: true,
  });
});

test("reload snapshots after an expired event cursor before resuming live updates", async ({
  page,
}) => {
  await openDashboard(page);
  const calls = [];
  let refused = false;
  let resumed = false;
  let resumedCursor = "";
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (
      /\/v1\.0\/api\/conversations\/[^/]+$/.test(url.pathname) &&
      request.method() === "GET"
    )
      calls.push("snapshot");
  });
  await page.route(
    /\/v1\.0\/api\/conversations\/[^/]+\/events(?:\?.*)?$/,
    async (route) => {
      calls.push("events");
      if (!refused) {
        refused = true;
        await route.fulfill({
          status: 409,
          contentType: "application/json",
          body: JSON.stringify({
            code: "event_history_gap",
            message: "The replay cursor expired.",
          }),
        });
      } else {
        resumed = true;
        resumedCursor = route.request().headers()["last-event-id"];
        await route.continue();
      }
    },
  );
  await page
    .getByRole("button", { name: "New conversation", exact: true })
    .first()
    .click();
  await page
    .getByRole("textbox", { name: "Question or title" })
    .fill("Replay snapshot recovery fixture");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect.poll(() => resumed).toBe(true);
  const first = calls.indexOf("events");
  const second = calls.indexOf("events", first + 1);
  expect(
    calls.slice(first + 1, second).filter((kind) => kind === "snapshot").length,
  ).toBeGreaterThanOrEqual(2);
  const id = new URL(page.url()).pathname.split("/").at(-1);
  expect(resumedCursor).toBe(id + ":1");
  await page.evaluate(async (conversation) => {
    const response = await fetch(
      `/v1.0/api/conversations/${conversation}/messages`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          action: "note",
          text: "Committed after replay recovery",
          idempotencyKey: "replay-recovery-note",
        }),
      },
    );
    if (!response.ok) throw new Error("Replay recovery fixture append failed.");
  }, id);
  await expect(
    page.getByText("Committed after replay recovery", { exact: true }),
  ).toBeVisible();
});
