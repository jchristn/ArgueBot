import { test as base, expect } from "@playwright/test";

/** The default administrator token. Every fixture service seeds it; the sign-in page shows it. */
export const defaultToken = "arguebotadmin";

/**
 * Pages start signed in with the default token unless a spec sets signedIn: false. API calls through
 * page.request and in-page fetch carry the same token through the configured Authorization header.
 */
export const test = base.extend({
  signedIn: [true, { option: true }],
  context: async ({ context, signedIn }, use) => {
    if (signedIn)
      await context.addInitScript((token) => {
        if (!sessionStorage.getItem("arguebot.session")) sessionStorage.setItem("arguebot.session", JSON.stringify({ token }));
      }, defaultToken);
    await use(context);
  },
});
export { expect };
