import { defineConfig } from "@playwright/test";
import path from "node:path";
const repo = path.resolve(import.meta.dirname, "..");
process.env.ARGUEBOT_E2E_HOME = path.join(repo, ".run/browser-home");
export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  timeout: 30000,
  outputDir: "../.run/browser-results",
  reporter: [
    ["list"],
    ["html", { outputFolder: "../.run/browser-report", open: "never" }],
  ],
  use: {
    baseURL: "https://127.0.0.1:4420",
    ignoreHTTPSErrors: true, // Disposable fixture certificate; no system trust changes.
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: {
    command:
      "node tests/start-server.mjs",
    url: "https://127.0.0.1:4420/health",
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 30000,
  },
});
