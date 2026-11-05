import { expect, it } from "vitest";
import { seatOptions } from "./seats.js";

const t = (key, values) => key + (values ? JSON.stringify(values) : "");
it("offers models and CLI harnesses with their sign-in, and flags directly configured CLI agents that have no stored secret", () => {
  const seats = seatOptions({
    secrets: [{ id: "sec_1", name: "Claude token" }],
    harnesses: [{ id: "har_1", name: "Claude Code", provider: "claude", credentialId: "sec_1" }, { id: "har_2", name: "Codex", provider: "codex", credentialId: "" }],
    endpoints: [{ id: "end_1", name: "Gateway", authBindings: [{ credentialId: "sec_2" }] }],
    models: [{ id: "mod_1", name: "Sonnet", harnessId: "har_1", model: "sonnet" }, { id: "mod_2", name: "Qwen", endpointId: "end_1", model: "qwen3" }],
    agents: [{ id: "par_1", name: "Reviewer", modelProfileId: "mod_1" }, { id: "par_2", name: "Inline CLI", kind: "cli", modelProfileId: "" }, { id: "par_3", name: "Inline REST", kind: "rest", provider: "ollama", model: "llama", modelProfileId: "" }],
  }, t);
  expect(seats.map((seat) => seat.id)).toEqual(["mod_1", "mod_2", "har_1", "har_2", "par_1", "par_2", "par_3"]);
  expect(seats.find((seat) => seat.id === "par_2")).toMatchObject({ warning: true });
  expect(seats.find((seat) => seat.id === "par_2").model).toContain("seat.hostLogin");
  expect(seats.find((seat) => seat.id === "har_1").model).toContain('seat.signedInWith{"secret":"Claude token"}');
  expect(seats.find((seat) => seat.id === "har_2")).toMatchObject({ warning: true });
  expect(seats.find((seat) => seat.id === "mod_2").model).toContain("seat.endpointAuth");
});
