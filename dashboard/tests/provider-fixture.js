import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";

// Reuse the committed Postman protocol simulation for real PolyPrompt browser journeys.
export async function startProviderFixture() {
  const source = await fs.readFile(path.resolve(import.meta.dirname, "../../postman/mocks/ollama/default.js"));
  const child = spawn(process.execPath, ["--input-type=commonjs"], { env: { ...process.env, PORT: "0" }, stdio: ["pipe", "pipe", "pipe"] });
  const stop = async () => {
    if (!child.pid || child.exitCode !== null || child.signalCode !== null) return;
    await new Promise((resolve) => {
      const timer = setTimeout(() => child.kill("SIGKILL"), 3000);
      child.once("exit", () => { clearTimeout(timer); resolve(); });
      child.kill("SIGTERM");
    });
  };
  try {
    const port = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("Provider fixture startup timed out.")), 5000);
      let pending = "";
      child.once("error", (error) => { clearTimeout(timer); reject(error); });
      child.once("exit", () => { clearTimeout(timer); reject(new Error("Provider fixture exited before readiness.")); });
      child.stdout.on("data", (chunk) => {
        pending += chunk.toString();
        if (!pending.includes("\n")) return;
        clearTimeout(timer);
        try { resolve(JSON.parse(pending.split("\n")[0]).port); } catch { reject(new Error("Provider fixture readiness was malformed.")); }
      });
      child.stdin.end(source);
    });
    if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error("Provider fixture returned an invalid port.");
    return { endpoint: `http://127.0.0.1:${port}`, stop };
  } catch (error) { await stop(); throw error; }
}
