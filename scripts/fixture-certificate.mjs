import fs from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
const root = path.resolve(import.meta.dirname, "..");

// Disposable loopback fixtures only. This helper never installs a trusted certificate.
export async function fixtureCertificate(home) {
  const directory = path.join(home, "keys");
  await fs.mkdir(directory, { recursive: true, mode: 0o700 });
  const file = path.resolve(directory, "listener.pfx");
  const fixture = path.join(
    root,
    "src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll",
  );
  const result = spawnSync("dotnet", [fixture, "certificate", file], {
    encoding: "utf8",
    input: "",
    timeout: 10000,
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error("Loopback certificate fixture failed: " + result.stderr);
  await fs.chmod(file, 0o600);
}
