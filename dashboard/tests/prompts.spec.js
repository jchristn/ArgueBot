import { test, expect } from "@playwright/test";

test("prompts page shows read-only factory defaults, edits a duplicate with the parameter legend, and conversations choose it", async ({ page, request }) => {
  test.setTimeout(90000);
  const suffix = Date.now();
  let copyId = "";
  try {
    for (const name of ["Prompt A", "Prompt B", "Prompt C"]) await request.post("/v1.0/api/participants", { data: { name: `${name} ${suffix}`, kind: "cli", provider: "claude" } });
    await page.goto("/dashboard/prompts");
    await expect(page.getByRole("heading", { name: "Prompts", exact: true })).toBeVisible();
    await page.getByRole("combobox", { name: "Debate step" }).selectOption("response");
    const factoryRow = page.getByRole("row").filter({ hasText: "Agent response (default)" });
    await expect(factoryRow).toContainText("Factory");
    await factoryRow.getByRole("button", { name: /^Actions for / }).click();
    await expect(page.getByRole("menuitem", { name: "Delete", exact: true })).toHaveCount(0);
    await page.getByRole("menuitem", { name: "View", exact: true }).click();
    const viewer = page.getByRole("dialog", { name: "Factory prompt" });
    await expect(viewer.getByLabel("Prompt text")).toHaveAttribute("readonly", "");
    await expect(viewer.getByRole("complementary", { name: "Parameters" })).toContainText("{current_question}");
    await expect(viewer.getByRole("complementary", { name: "Parameters" })).toContainText("Required");
    await expect(viewer.getByRole("complementary", { name: "Parameters" })).not.toContainText("{proposal_id}");
    await page.screenshot({ path: "../.run/screenshots/prompt-factory.png" });
    await viewer.getByRole("button", { name: "Duplicate to edit", exact: true }).click();

    const editor = page.getByRole("dialog", { name: "Edit prompt" });
    await expect(editor.getByLabel("Name", { exact: true })).toHaveValue("Agent response (copy)");
    await editor.getByLabel("Name", { exact: true }).fill(`Terse ${suffix}`);
    const body = editor.getByLabel("Prompt text");
    await body.fill("Answer briefly. ");
    await expect(editor.getByRole("status")).toContainText("must include {current_question}");
    await expect(editor.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
    await body.press("End");
    await editor.getByRole("button", { name: "{current_question}", exact: true }).click();
    await expect(body).toHaveValue("Answer briefly. {current_question}");
    await body.press("End");
    await body.type(" Mention {context}.");
    await expect(editor.getByRole("status")).toContainText("{context} is not a parameter");
    await page.screenshot({ path: "../.run/screenshots/prompt-editor.png" });
    await body.fill("Answer briefly. {current_question}");
    await editor.getByRole("button", { name: "Save", exact: true }).click();
    await expect(editor).toHaveCount(0);
    const copy = (await (await request.get("/v2.0/api/prompts?purpose=response")).json()).find(item => item.name === `Terse ${suffix}`);
    copyId = copy.id;
    expect(copy.factory).toBe(false);
    await expect(page.getByRole("row").filter({ hasText: `Terse ${suffix}` })).toContainText("Custom");

    await page.goto("/dashboard/conversations");
    await page.getByRole("button", { name: "New conversation", exact: true }).click();
    const create = page.getByRole("dialog", { name: "New conversation" });
    await create.getByRole("textbox", { name: "Title", exact: true }).fill(`Prompted ${suffix}`);
    await create.getByText("Debate prompts").click();
    await create.getByRole("combobox", { name: "Agent response" }).selectOption({ label: `Terse ${suffix}` });
    await expect(create.locator(".prompt-picker summary")).toContainText("1 custom prompt");
    await create.getByRole("button", { name: "Create conversation", exact: true }).click();
    await expect(page.getByRole("heading", { name: `Prompted ${suffix}`, exact: true })).toBeVisible();
    const id = page.url().split("/").at(-1);
    expect((await (await request.get(`/v1.0/api/conversations/${id}`)).json()).promptIds).toEqual({ response: copyId });
    await page.getByRole("button", { name: "Start debate" }).first().click();
    await expect(page.getByRole("dialog").getByRole("combobox", { name: "Agent response" })).toHaveValue(copyId);
    await page.keyboard.press("Escape");
    await request.delete(`/v1.0/api/conversations/${id}`);
  } finally {
    if (copyId) {
      const current = await (await request.get(`/v2.0/api/prompts/${copyId}`)).json();
      await request.delete(`/v2.0/api/prompts/${copyId}?revision=${current.revision}`);
    }
  }
});
