import { expect, it } from "vitest";
import { seatOptions } from "./seats.js";

const t = (key, values) => key + (values ? JSON.stringify(values) : "");
it("lists agents with their program, model and sign-in", () => {
  const seats = seatOptions({
    secrets: [{ id: "sec_1", name: "Claude token" }],
    models: [{ id: "mod_2", name: "Qwen", endpointId: "end_1", model: "qwen3" }],
    agents: [
      { id: "par_1", name: "Skeptic", kind: "cli", provider: "claude", executable: "claude", model: "sonnet", credentialId: "sec_1" },
      { id: "par_2", name: "Local Codex", kind: "cli", provider: "codex", executable: "codex", model: "", credentialId: "" },
      { id: "par_3", name: "Reviewer", modelProfileId: "mod_2" },
      { id: "par_4", name: "Orphan", modelProfileId: "mod_gone" },
    ],
  }, t);
  expect(seats.map((seat) => seat.id)).toEqual(["par_1", "par_2", "par_3", "par_4"]);
  expect(seats[0].model).toBe('Claude Code · sonnet · seat.signedInWith{"secret":"Claude token"}');
  expect(seats[1].model).toContain("seat.hostLogin");
  expect(seats[1].warning).toBe(false);
  expect(seats[2].model).toContain('seat.model{"name":"Qwen"}');
  expect(seats[3]).toMatchObject({ warning: true });
});
