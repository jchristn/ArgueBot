import { test, expect } from "./fixtures.js";
import { startProviderFixture } from "./provider-fixture.js";

async function post(request, path, data) {
  const response = await request.post(path, { data });
  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}

// A user creates a resource, runs a debate whose agents read it and append a note, then reviews and reverts the note.
test("resource walkthrough: create, agents read and write, review history and revert", async ({ page, request }) => {
  test.setTimeout(90000);
  const fixture = await startProviderFixture();
  const suffix = Date.now().toString();
  const billing = "billing-" + suffix;
  const notes = "notes-" + suffix;
  try {
    await page.goto("/dashboard/resources");
    await expect(page.getByRole("heading", { name: "Resources", level: 1 })).toBeVisible();
    for (const [name, title, body] of [[billing, "Billing", "# Billing\n\nInvoices go out on the 1st."], [notes, "Debate notes", "Notes:"]]) {
      await page.getByRole("button", { name: "Add resource", exact: true }).click();
      const dialog = page.getByRole("dialog");
      await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
      await dialog.getByRole("textbox", { name: "Title", exact: true }).fill(title);
      await dialog.getByRole("textbox", { name: "Description", exact: true }).fill(title + " for the walkthrough.");
      await dialog.getByRole("textbox", { name: "Markdown", exact: true }).fill(body);
      await dialog.getByRole("tab", { name: "Preview" }).click();
      await expect(dialog.locator(".resource-preview")).toContainText(name === billing ? "Invoices go out on the 1st." : "Notes:");
      await dialog.getByRole("button", { name: "Save", exact: true }).click();
      await expect(dialog).toContainText("Revision 1");
      await dialog.getByRole("button", { name: "Close", exact: true }).last().click();
      await expect(page.getByRole("row").filter({ hasText: name })).toBeVisible();
    }
    await page.screenshot({ path: "../.run/screenshots/v2.2-resources.png", fullPage: true });

    // Agents get role instructions that name both resources; the debate allows writing.
    const role = `If you need billing details, call read_resource with name ${billing} and then append_resource with name ${notes} to record what you found`;
    const profiles = [];
    for (const name of ["Reader A", "Reader B", "Moderator"]) profiles.push(await post(request, "/v2.0/api/participants", { name: name + " " + suffix, kind: "rest", provider: "ollama", endpoint: fixture.endpoint, model: "multi-agent-resources", roleInstructions: role }));
    const conversation = await post(request, "/v2.0/api/conversations", { title: "Resource walkthrough " + suffix });
    await page.goto(`/dashboard/conversations/${conversation.id}`);
    await page.getByRole("button", { name: "Start another debate", exact: true }).click();
    const start = page.getByRole("dialog");
    for (const profile of profiles.slice(0, 2)) await start.getByLabel(profile.name, { exact: false }).check();
    await start.getByRole("combobox", { name: "Orchestrator model" }).selectOption(profiles[2].id);
    await start.getByRole("spinbutton", { name: "Maximum rounds" }).fill("1");
    await start.getByRole("spinbutton", { name: "Pause between turns (seconds)" }).fill("0");
    await start.locator(".debate-policy summary").click();
    await expect(start.getByRole("combobox", { name: "Resource access", exact: true })).toHaveValue("read");
    await start.getByRole("combobox", { name: "Resource access", exact: true }).selectOption("read_write");
    await start.getByRole("textbox", { name: "Only these resources", exact: true }).fill(`${billing}, ${notes}`);
    // This walkthrough is about resources; start the panel without the orchestrator's opening questions.
    await start.getByRole("combobox", { name: "Opening questions", exact: true }).selectOption("off");
    await expect(start.getByRole("spinbutton", { name: "Resource writes per turn", exact: true })).toHaveValue("8");
    await start.getByRole("button", { name: "Start debate", exact: true }).click();
    await expect(start).toHaveCount(0);
    const runsPath = `/v2.0/api/conversations/${conversation.id}/runs`;
    await expect.poll(async () => (await (await request.get(runsPath)).json()).at(-1)?.state, { timeout: 45000 }).toBe("completed");
    const run = (await (await request.get(runsPath)).json()).at(-1);
    expect(run.policy.resourceAccess).toBe("read_write");
    expect(run.policy.resourceNames).toEqual([billing, notes]);
    const attempts = await (await request.get(`${runsPath}/${run.id}/attempts?pageSize=100`)).json();
    expect(attempts.items.filter(item => item.purpose === "response").every(item => item.resources.some(use => use.name === billing && use.access === "read"))).toBeTruthy();

    // The transcript links each resource call to the revision it used.
    await page.reload();
    await expect(page.getByText("Billing context read: invoices go out on the 1st.").first()).toBeVisible();
    await page.getByRole("button", { name: /2 tool calls read_resource · append_resource/ }).first().click();
    await page.locator("details.tool-evidence summary").filter({ hasText: "append_resource" }).first().click();
    const link = page.getByRole("link", { name: new RegExp(`^Open ${notes} at revision \\d+$`) }).first();
    await link.click();
    const editor = page.getByRole("dialog", { name: "Edit resource" });
    await expect(editor.getByRole("tab", { name: "History" })).toHaveAttribute("aria-selected", "true");
    await expect(editor.getByText("Agent: Reader", { exact: false }).first()).toBeVisible();
    await page.screenshot({ path: "../.run/screenshots/v2.2-resource-history.png", fullPage: true });

    // Review: the agents appended notes; restore the user's revision 1 as a new revision.
    const current = await (await request.get("/v2.0/api/resources/by-name/" + notes)).json();
    expect(current.updatedByKind).toBe("agent");
    expect(current.body).toContain("REST note.");
    await editor.getByRole("button", { name: /^Revision 1/ }).click();
    await expect(editor.locator(".diff-added").first()).toContainText("REST note.");
    await editor.getByRole("button", { name: "Restore this revision", exact: true }).click();
    await expect(editor.getByRole("tab", { name: "Edit" })).toHaveAttribute("aria-selected", "true");
    await expect(editor.getByRole("textbox", { name: "Markdown", exact: true })).toHaveValue("Notes:");
    await editor.getByRole("button", { name: "Close", exact: true }).last().click();
    const reverted = await (await request.get("/v2.0/api/resources/by-name/" + notes)).json();
    expect(reverted.body).toBe("Notes:");
    expect(reverted.updatedByKind).toBe("user");
    expect(reverted.revision).toBe(current.revision + 1);
  } finally {
    await fixture.stop();
  }
});
