import { test, expect } from "./fixtures.js";
import path from "node:path";

test("a real CLI deadline exposes its cause and frozen settings beside the partial answer", async ({ page, request }) => {
  const profileResponse = await request.post("/v1.0/api/participants", { data: {
    name: "Deadline browser fixture", kind: "cli", provider: "claude", executable: "dotnet",
    arguments: [path.resolve("../src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll"), "diagnostic-timeout"],
    timeoutSeconds: 2,
  } });
  expect(profileResponse.status()).toBe(201);
  const profile = await profileResponse.json();
  const conversation = await (await request.post("/v1.0/api/conversations", { data: { title: "Deadline browser evidence" } })).json();
  const root = `/v1.0/api/conversations/${conversation.id}`;
  const response = await request.post(`${root}/runs`, { data: {
    mode: "followup", question: "Fictional deadline browser input", participantIds: [profile.id], idempotencyKey: "browser-deadline",
  } });
  expect(response.status()).toBe(202);
  const run = await response.json();
  await page.goto(`/dashboard/conversations/${conversation.id}`);
  await expect(page.getByRole("status").filter({ hasText: "per-turn limit" })).toContainText("2s");
  const warning = page.getByRole("alert").filter({ hasText: "per-turn time limit" });
  await expect(warning).toContainText("generation_timeout");
  await expect(warning).toContainText("Resume keeps the original timeout");
  await page.reload();
  await expect(warning).toContainText("generation_timeout");
  const attempt = (await (await request.get(`${root}/runs/${run.id}/attempts`)).json()).items[0];
  expect(attempt.state).toBe("failed");
  expect(attempt.errorCode).toBe("generation_timeout");
  expect(attempt.durationMilliseconds).toBeGreaterThanOrEqual(1800);
  expect((await request.post(`${root}/runs/${run.id}/actions`, { data: { action: "stop", idempotencyKey: "stop-deadline" } })).status()).toBe(200);
  const newer = await (await request.post(`${root}/runs`, { data: { mode: "followup", question: "Fictional newer run", participantIds: [profile.id], idempotencyKey: "browser-newer" } })).json();
  expect((await request.post(`${root}/runs/${newer.id}/actions`, { data: { action: "stop", idempotencyKey: "stop-newer" } })).status()).toBe(200);
  await page.reload();
  await warning.getByRole("button", { name: "Generation attempts", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("combobox", { name: "Run", exact: true })).toHaveValue(run.id);
  await dialog.getByRole("button", { name: /^Actions for Attempt/ }).first().click();
  await page.getByRole("menuitem", { name: "Inspect attempt", exact: true }).click();
  await expect(dialog.getByText("Frozen per-turn timeout (seconds)", { exact: true })).toBeVisible();
  await expect(dialog.getByText(attempt.errorMessage, { exact: true })).toBeVisible();
  await page.screenshot({ path: "../.run/screenshots/generation-deadline.png", fullPage: true });
});
