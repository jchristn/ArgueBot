import { test, expect, defaultToken } from "./fixtures.js";

test.use({ signedIn: false, extraHTTPHeaders: {} });
const admin = { Authorization: "Bearer " + defaultToken };

test("sign-in page always shows the default credentials, returns to the requested page and signs out", async ({ page }) => {
  await page.goto("/dashboard/conversations");
  await expect(page).toHaveURL(/\/dashboard\/login$/);
  await expect(page.getByRole("heading", { name: "Sign in", exact: true })).toBeVisible();
  const defaults = page.getByRole("complementary", { name: "Default administrator" });
  for (const value of ["admin@arguebot", "password", "arguebotadmin"]) await expect(defaults.getByText(value, { exact: true })).toBeVisible();
  await page.getByLabel("Email", { exact: true }).fill("admin@arguebot");
  await page.getByLabel("Password", { exact: true }).fill("not-the-password");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("The email or password is not correct.");
  await expect(defaults.getByRole("button")).toHaveCount(0);
  await page.getByLabel("Email", { exact: true }).fill("admin@arguebot");
  await page.getByLabel("Password", { exact: true }).fill("password");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/conversations$/);
  const account = page.getByRole("button", { name: "Account and sign out" });
  await expect(account).toContainText("Default tenant");
  // The account button sizes to its name and tenant instead of clipping them at the icon-button width.
  for (const width of [1280, 768]) {
    await page.setViewportSize({ width, height: 800 });
    const box = await account.evaluate(element => ({ clipped: element.scrollWidth > element.clientWidth + 1, right: element.getBoundingClientRect().right, viewport: window.innerWidth, width: element.getBoundingClientRect().width }));
    expect(box.clipped, `account button clipped at ${width}`).toBe(false);
    expect(box.width).toBeGreaterThan(60);
    expect(box.right).toBeLessThanOrEqual(box.viewport);
    await page.locator(".topbar").screenshot({ path: `../.run/screenshots/topbar-account-${width}.png` });
  }
  expect(await page.evaluate(() => JSON.parse(sessionStorage.getItem("arguebot.session")).token.startsWith("abs_"))).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem("arguebot.session"))).toBeNull();
  await page.reload();
  await expect(account).toBeVisible();
  for (const name of ["Users", "Tenants", "Audit log", "Settings"]) await expect(page.getByRole("link", { name, exact: true })).toBeVisible();
  await account.click();
  await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/login$/);
  await page.goto("/dashboard/home");
  await expect(page).toHaveURL(/\/dashboard\/login$/);
});

test("a bearer token signs in, and members see no administration", async ({ page, request }) => {
  await page.goto("/dashboard/login");
  await page.getByRole("button", { name: "Sign in with a bearer token instead" }).click();
  await page.getByLabel("Bearer token", { exact: true }).fill(defaultToken);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/home$/);
  await page.getByRole("button", { name: "Account and sign out" }).click();
  await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
  const email = `member-${Date.now()}@example.test`;
  const created = await request.post("/v2.0/api/users", { headers: admin, data: { firstName: "Fixture", lastName: "Member", email, password: "member-password" } });
  expect(created.status()).toBe(201);
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill("member-password");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/home$/);
  await expect(page.getByRole("button", { name: "Account and sign out" })).toContainText("Fixture Member");
  for (const name of ["Users", "Tenants", "Audit log", "Settings", "Request History"]) await expect(page.getByRole("link", { name, exact: true })).toHaveCount(0);
  await page.goto("/dashboard/settings");
  await expect(page).toHaveURL(/\/dashboard\/home$/);
});

test("account page creates, reveals and disables a bearer token, and administrators add users", async ({ page, request, context }) => {
  await context.addInitScript((token) => sessionStorage.setItem("arguebot.session", JSON.stringify({ token })), defaultToken);
  await page.goto("/dashboard/account");
  await expect(page.getByRole("heading", { name: "Account", exact: true })).toBeVisible();
  const name = "Browser token " + Date.now();
  await page.getByRole("button", { name: "Create token", exact: true }).click();
  await page.getByLabel("Name", { exact: true }).fill(name);
  await page.getByRole("dialog").getByRole("button", { name: "Create", exact: true }).click();
  const shown = page.getByRole("dialog", { name: "Copy your new token" });
  const token = (await shown.locator("code").textContent()).trim();
  expect(token.startsWith("abt_")).toBe(true);
  await shown.getByRole("button", { name: "I copied it" }).click();
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toContainText("••••" + token.slice(-4));
  await expect(page.getByText(token, { exact: true })).toHaveCount(0);
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Reveal", exact: true }).click();
  await page.getByRole("dialog", { name: "Reveal token" }).getByRole("button", { name: "Reveal", exact: true }).click();
  const revealed = page.getByRole("dialog", { name: "Bearer token" });
  await expect(revealed.locator("code")).toHaveText(token);
  await revealed.getByRole("button", { name: "Close", exact: true }).last().click();
  const details = await request.get("/v2.0/api/token/details", { headers: { Authorization: "Bearer " + token } });
  expect((await details.json()).principalType).toBe("Credential");
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Disable", exact: true }).click();
  await expect(row).toContainText("Disabled");
  expect((await request.get("/v2.0/api/token/details", { headers: { Authorization: "Bearer " + token } })).status()).toBe(401);

  await page.goto("/dashboard/users");
  const email = `browser-${Date.now()}@example.test`;
  await page.getByRole("button", { name: "Add user", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Add user" });
  await dialog.getByLabel("First name", { exact: true }).fill("Browser");
  await dialog.getByLabel("Email", { exact: true }).fill(email);
  await dialog.getByLabel("Password", { exact: true }).fill("browser-password");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(page.getByRole("row").filter({ hasText: email })).toContainText("Member");
  const login = await request.post("/v2.0/api/token", { data: { email, password: "browser-password" } });
  expect(login.status()).toBe(201);
  await page.goto("/dashboard/audit");
  await expect(page.getByRole("cell", { name: "UserCreated", exact: true }).first()).toBeVisible();
});

test("sign-in page fits narrow and wide screens in both themes and right-to-left", async ({ page }) => {
  for (const locale of ["en", "ar-XB"]) for (const theme of ["light", "dark"]) for (const width of [390, 1280]) {
    await page.addInitScript(([locale, theme]) => { localStorage.setItem("arguebot.locale", locale); localStorage.setItem("arguebot.theme", theme); }, [locale, theme]);
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/dashboard/login");
    await expect(page.locator(".default-credentials")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    expect(await page.evaluate(() => [...document.querySelectorAll("input, button, select")].filter(element => !element.getAttribute("aria-label") && !element.labels?.length && !element.textContent.trim()).length)).toBe(0);
    await page.screenshot({ path: `../.run/screenshots/sign-in-${locale}-${theme}-${width}.png`, fullPage: true });
  }
});

test("system administrators create a tenant and its admin; a shared email chooses a tenant, changes its password and is sent back to sign-in when revoked", async ({ page, request, context }) => {
  await context.addInitScript((token) => { if (!sessionStorage.getItem("arguebot.session")) sessionStorage.setItem("arguebot.session", JSON.stringify({ token })); }, defaultToken);
  const stamp = Date.now();
  const tenantName = "Browser tenant " + stamp;
  const email = `shared-${stamp}@example.test`;
  await page.goto("/dashboard/tenants");
  await page.getByRole("button", { name: "Add tenant", exact: true }).click();
  await page.getByRole("dialog").getByLabel("Name", { exact: true }).fill(tenantName);
  await page.getByRole("dialog").getByRole("button", { name: "Create", exact: true }).click();
  const row = page.getByRole("row").filter({ hasText: tenantName });
  await row.getByText(tenantName, { exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Edit tenant" })).toBeVisible();
  await page.keyboard.press("Escape");
  await row.getByRole("button", { name: /^Actions for / }).click();
  await page.getByRole("menuitem", { name: "Users", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/users\?tenant=ten_/);
  await expect(page.getByRole("heading", { name: "Users", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Add user", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Add user" });
  await dialog.getByLabel("Email", { exact: true }).fill(email);
  await dialog.getByLabel("Password", { exact: true }).fill("shared-password");
  await dialog.getByRole("checkbox", { name: "Tenant administrator" }).check();
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(page.getByRole("row").filter({ hasText: email })).toContainText("Tenant administrator");
  // The same email and password in the default tenant makes sign-in ask for a tenant.
  expect((await request.post("/v2.0/api/users", { headers: admin, data: { email, password: "shared-password" } })).status()).toBe(201);
  await page.getByRole("button", { name: "Account and sign out" }).click();
  await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
  await page.evaluate(() => sessionStorage.setItem("arguebot.session", "null"));
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill("shared-password");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Choose a tenant");
  await page.getByLabel("Tenant", { exact: true }).selectOption({ label: tenantName });
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/home$/);
  await expect(page.getByRole("button", { name: "Account and sign out" })).toContainText(tenantName);
  await expect(page.getByRole("link", { name: "Users", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Tenants", exact: true })).toHaveCount(0);
  // Password change keeps this session.
  await page.goto("/dashboard/account");
  await page.getByLabel("Current password", { exact: true }).fill("shared-password");
  await page.getByLabel("New password", { exact: true }).fill("shared-password-2");
  await page.getByLabel("Repeat new password", { exact: true }).fill("shared-password-2");
  await page.getByRole("button", { name: "Change password", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Password changed" })).toBeVisible();
  // Revoking the session elsewhere sends the next request back to sign-in, with a reason.
  const stored = await page.evaluate(() => JSON.parse(sessionStorage.getItem("arguebot.session")));
  const sessions = await (await request.get("/v2.0/api/me/sessions", { headers: { Authorization: "Bearer " + stored.token } })).json();
  expect((await request.delete("/v2.0/api/token", { headers: { Authorization: "Bearer " + stored.token } })).ok()).toBe(true);
  expect(sessions.length).toBe(1);
  await page.getByRole("link", { name: "Conversations", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/login$/);
  await expect(page.getByRole("alert")).toContainText("Your session ended");
});
