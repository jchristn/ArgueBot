import { test, expect } from "./fixtures.js";
import path from "node:path";

async function runningConversation(request, title) {
  const profile = await (await request.post("/v1.0/api/participants", { data: {
    name: title + " agent", kind: "cli", provider: "claude", executable: "dotnet",
    arguments: [path.resolve("../src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll"), "diagnostic-timeout"], timeoutSeconds: 120,
  } })).json();
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title } })).json();
  const run = await request.post(`/v1.0/api/conversations/${conversation.id}/runs`, { data: { mode: "followup", question: "Fictional long turn", participantIds: [profile.id], idempotencyKey: "stop-" + conversation.id } });
  expect(run.status()).toBe(202);
  return { conversation, profile };
}

test("conversation menu stops a debate, and deleting an active one explains and offers stop-and-delete in the dialog", async ({ page, request }) => {
  test.setTimeout(120000);
  const suffix = Date.now();
  const busy = await runningConversation(request, "Stop and delete " + suffix);
  const stopped = await runningConversation(request, "Stop only " + suffix);
  try {
    await page.goto("/dashboard/conversations");
    await page.getByRole("searchbox").fill(String(suffix));
    const busyRow = page.getByRole("row").filter({ hasText: busy.conversation.id });
    await busyRow.getByRole("button", { name: /^Actions for / }).click();
    await expect(page.getByRole("menuitem", { name: "Stop debate", exact: true })).toBeVisible();
    await page.getByRole("menuitem", { name: "Delete", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Delete" });
    await dialog.getByRole("button", { name: "Delete", exact: true }).click();
    await expect(dialog.getByRole("alert")).toContainText("unfinished debate");
    await expect(page.locator(".toast")).toHaveCount(0);
    await page.screenshot({ path: "../.run/screenshots/conversation-delete-active.png" });
    await dialog.getByRole("button", { name: "Stop debate and delete", exact: true }).click();
    await expect(dialog).toHaveCount(0, { timeout: 40000 });
    await expect(page.getByRole("row").filter({ hasText: busy.conversation.id })).toHaveCount(0);
    expect((await request.get(`/v1.0/api/conversations/${busy.conversation.id}`)).status()).toBe(404);

    const stoppedRow = page.getByRole("row").filter({ hasText: stopped.conversation.id });
    await stoppedRow.getByRole("button", { name: /^Actions for / }).click();
    await page.getByRole("menuitem", { name: "Stop debate", exact: true }).click();
    const confirm = page.getByRole("dialog", { name: "Stop debate" });
    await expect(confirm).toContainText("Partial answers stay in history");
    await confirm.getByRole("button", { name: "Stop debate", exact: true }).click();
    await expect(confirm).toHaveCount(0, { timeout: 40000 });
    const runs = await (await request.get(`/v1.0/api/conversations/${stopped.conversation.id}/runs`)).json();
    expect(["cancelled", "completed", "failed"]).toContain(runs.at(-1).state);
    await stoppedRow.getByRole("button", { name: /^Actions for / }).click();
    await expect(page.getByRole("menuitem", { name: "Stop debate", exact: true })).toHaveCount(0);
    await page.keyboard.press("Escape");
  } finally {
    await request.delete(`/v1.0/api/conversations/${stopped.conversation.id}`);
    for (const item of [busy, stopped]) await request.delete(`/v1.0/api/participants/${item.profile.id}`);
  }
});

test("notifications appear above an open dialog", async ({ page }) => {
  await page.goto("/dashboard/conversations");
  await page.getByRole("button", { name: "New conversation", exact: true }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  // Raise an application notice while the dialog is open by failing a request the dialog makes.
  await page.route("**/v1.0/api/conversations", route => route.request().method() === "POST" ? route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ code: "fixture_failure", message: "Fixture failure while a dialog is open." }) }) : route.continue());
  await page.getByRole("dialog").getByRole("textbox", { name: "Title", exact: true }).fill("Toast layering check");
  await page.getByRole("dialog").getByRole("button", { name: /Create|Save/ }).click();
  const toast = page.locator(".toast");
  await expect(toast).toContainText("Fixture failure");
  const box = await toast.boundingBox();
  const top = await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.closest(".toast") !== null, { x: box.x + 10, y: box.y + box.height / 2 });
  expect(top).toBe(true);
  await toast.getByRole("button", { name: "Dismiss" }).click();
  await expect(toast).toHaveCount(0);
  await expect(page.getByRole("dialog")).toBeVisible();
});
