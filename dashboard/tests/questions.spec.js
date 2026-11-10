import { test, expect } from "./fixtures.js";
import { startProviderFixture } from "./provider-fixture.js";

async function post(request, path, data) {
  const response = await request.post(path, { data });
  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}

async function panel(request, fixture, suffix) {
  const asker = await post(request, "/v2.0/api/participants", { name: "Asker " + suffix, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent-questions", supportsTools: true });
  const member = await post(request, "/v2.0/api/participants", { name: "Panelist " + suffix, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent", supportsTools: true });
  const orchestrator = await post(request, "/v2.0/api/participants", { name: "Orchestrator " + suffix, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent", supportsTools: true });
  return { asker, member, orchestrator };
}

async function start(request, conversation, seats, key) {
  return post(request, `/v2.0/api/conversations/${conversation.id}/runs`, { protocolVersion: 2, orchestratorId: seats.orchestrator.id, participantIds: [seats.asker.id, seats.member.id], maxRounds: 1, interventionSeconds: 0, policy: { intake: "off", questionHoldSeconds: 120 }, idempotencyKey: key });
}

async function runState(request, conversation, run) {
  return (await (await request.get(`/v2.0/api/conversations/${conversation.id}/runs/${run.id}`)).json()).state;
}

// The agent's question opens in a dialog on the waiting conversation; the user answers there and the debate finishes. Nothing is answered inline.
test("questions: the dialog opens on the waiting conversation and the answer finishes the debate", async ({ page, request }) => {
  test.setTimeout(90000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  try {
    const seats = await panel(request, fixture, suffix);
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Question walkthrough " + suffix });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    const run = await start(request, conversation, seats, "question-" + suffix);
    const dialog = page.getByRole("dialog", { name: "An agent has a question" });
    await expect(dialog).toBeVisible({ timeout: 20000 });
    await expect(dialog).toContainText("Which environments should the rollout cover?");
    await expect(dialog).toContainText("Recommended");
    await expect(page.locator(".waiting-banner")).toHaveCount(1);
    await expect(page.locator(".waiting-banner").getByRole("radio")).toHaveCount(0);
    await page.screenshot({ path: "../.run/screenshots/v2.3-question-modal.png", fullPage: true });
    await dialog.getByRole("button", { name: "Send answers" }).click();
    await expect(dialog.locator(".error-summary")).toBeVisible();
    await dialog.getByRole("radio", { name: /^Production only/ }).check();
    await dialog.getByRole("button", { name: "Send answers" }).click();
    await expect(dialog).toContainText("Answered");
    await dialog.getByRole("button", { name: "Close" }).last().click();
    await expect.poll(() => runState(request, conversation, run), { timeout: 45000 }).toBe("completed");
    await expect(page.locator("main")).toContainText("Using your answer (Production only)", { timeout: 15000 });
    const questions = await (await request.get(`/v2.0/api/conversations/${conversation.id}/runs/${run.id}/questions`)).json();
    expect(questions).toHaveLength(1);
    expect(questions[0].status).toBe("answered");
    expect(questions[0].deletable).toBe(true);

    // The finished debate's answered question can be selected on the Questions page and deleted; the answer stays in the transcript.
    await page.goto("/dashboard/questions");
    await page.getByLabel("Status", { exact: true }).selectOption("resolved");
    const row = page.getByRole("row").filter({ hasText: "Question walkthrough " + suffix });
    await expect(row).toHaveCount(1);
    const box = row.getByRole("checkbox");
    await expect(box).toBeEnabled();
    expect((await row.locator("td.select-cell").boundingBox()).width).toBeLessThan(48);
    await box.check();
    await page.getByRole("button", { name: "Delete multiple (1)", exact: true }).click();
    const confirm = page.getByRole("dialog", { name: "Delete", exact: true });
    await expect(confirm).toContainText("Question walkthrough " + suffix);
    await confirm.getByRole("button", { name: "Delete 1 item", exact: true }).click();
    await expect(confirm).toHaveCount(0);
    await expect(row).toHaveCount(0);
    expect((await request.get(`/v2.0/api/questions/${questions[0].id}`)).status()).toBe(404);
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    await expect(page.locator("main")).toContainText("Production only", { timeout: 15000 });
  } finally {
    await fixture.stop();
  }
});

// Elsewhere a toast offers the question; closing the dialog never skips it; Answer later parks the turn; the inbox reopens it; a second tab sees the conflict.
test("questions: toast, close without skipping, answer later, inbox and a conflicting second tab", async ({ page, request }) => {
  test.setTimeout(120000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  try {
    const seats = await panel(request, fixture, suffix);
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Question inbox " + suffix });
    // The dashboard notifies about questions asked after it loaded; wait for its first change-feed read.
    const watching = page.waitForResponse(response => response.url().includes("/v2.0/api/questions/events"));
    await page.goto("/dashboard/home");
    await watching;
    const run = await start(request, conversation, seats, "inbox-" + suffix);
    const toast = page.locator(".question-toast");
    await expect(toast).toBeVisible({ timeout: 20000 });
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await toast.getByRole("button", { name: "Answer", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "An agent has a question" });
    await expect(dialog).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    const [request0] = (await (await request.get(`/v2.0/api/questions?status=open&scope=tenant&conversationId=${conversation.id}`)).json()).items;
    expect(request0.status).toBe("open");
    await page.goto("/dashboard/questions");
    await expect(page.getByRole("heading", { name: "Questions", level: 1 })).toBeVisible();
    await page.getByRole("cell", { name: "Asker " + suffix, exact: true }).click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: "Answer later" }).click();
    await expect(dialog).toHaveCount(0);
    await expect.poll(() => runState(request, conversation, run), { timeout: 20000 }).toBe("awaiting_input");
    await expect.poll(async () => (await (await request.get(`/v2.0/api/questions/${request0.id}`)).json()).holdState, { timeout: 20000 }).toBe("parked");

    const second = await page.context().newPage();
    await second.goto("/dashboard/questions");
    for (const target of [page, second]) {
      await target.reload();
      await target.getByRole("cell", { name: "Asker " + suffix, exact: true }).click();
      await expect(target.getByRole("dialog", { name: "An agent has a question" })).toBeVisible();
    }
    await second.getByRole("dialog").getByRole("radio", { name: /^Every environment/ }).check();
    await second.getByRole("dialog").getByRole("button", { name: "Send answers" }).click();
    await expect(second.getByRole("dialog")).toContainText("Answered");
    await dialog.getByRole("radio", { name: /^Production only/ }).check();
    await dialog.getByRole("button", { name: "Send answers" }).click();
    await expect(dialog).toContainText("Someone else resolved this question first");
    await second.close();
    await expect.poll(() => runState(request, conversation, run), { timeout: 45000 }).toBe("completed");
    const [resolved] = await (await request.get(`/v2.0/api/conversations/${conversation.id}/runs/${run.id}/questions`)).json();
    expect(resolved.questions[0].answer.selectedLabels).toEqual(["Every environment"]);
    expect(resolved.resumeAttemptId).not.toBe("");
  } finally {
    await fixture.stop();
  }
});

// The question dialog fits phone, tablet and desktop widths in both themes.
test("questions: the dialog renders at three widths in light and dark themes", async ({ page, request }) => {
  test.setTimeout(90000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  try {
    const seats = await panel(request, fixture, suffix);
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Question layout " + suffix });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    await start(request, conversation, seats, "layout-" + suffix);
    const dialog = page.getByRole("dialog", { name: "An agent has a question" });
    await expect(dialog).toBeVisible({ timeout: 20000 });
    for (const theme of ["light", "dark"]) {
      await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
      for (const [width, height] of [[1280, 900], [768, 1024], [390, 844]]) {
        await page.setViewportSize({ width, height });
        const box = await dialog.boundingBox();
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(width + 1);
        await expect(dialog.getByRole("button", { name: "Send answers" })).toBeVisible();
        await page.screenshot({ path: `../.run/screenshots/v2.3-question-${theme}-${width}.png` });
      }
    }
    await dialog.getByRole("button", { name: "Skip" }).click();
    await dialog.getByRole("button", { name: "Skip the question" }).click();
    await expect(dialog).toContainText("Skipped");
  } finally {
    await fixture.stop();
  }
});
