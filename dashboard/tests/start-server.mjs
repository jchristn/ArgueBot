import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import { fixtureCertificate } from "../../scripts/fixture-certificate.mjs";
const root = path.resolve(import.meta.dirname, "../..");
const home = path.join(root, ".run/browser-home");
await fixtureCertificate(home);
const file = path.join(home, "arguebot.json");
let settings = {};
try {
  settings = JSON.parse(await fs.readFile(file, "utf8"));
} catch (error) {
  if (error.code !== "ENOENT") throw error;
}
await fs.writeFile(
  file,
  JSON.stringify({ ...settings, port: 4420, tlsEnabled: true, requireAuthentication: false }),
  { mode: 0o600 },
);
const child = spawn(
  "dotnet",
  [
    "run",
    "--project",
    "src/ArgueBot.Server",
    "-c",
    "Release",
    "-f",
    "net10.0",
    "--no-build",
    "--",
    "--home",
    home,
    "--port",
    "4420",
  ],
  { cwd: root, stdio: "inherit" },
);
for (const signal of ["SIGTERM", "SIGINT"])
  process.on(signal, () => child.kill(signal));
child.on("error", (error) => {
  process.stderr.write(error.message + "\n");
  process.exitCode = 1;
});
child.on("exit", (code) => {
  process.exitCode = code ?? 0;
});
