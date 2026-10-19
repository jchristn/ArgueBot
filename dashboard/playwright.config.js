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
    baseURL: "http://127.0.0.1:4420",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: {
    command:
      "dotnet run --project ../src/ArgueBot.Server -c Release -f net10.0 --no-build -- --home ../.run/browser-home --port 4420",
    url: "http://127.0.0.1:4420/health",
    reuseExistingServer: false,
    timeout: 30000,
  },
});
