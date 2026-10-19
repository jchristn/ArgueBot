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
