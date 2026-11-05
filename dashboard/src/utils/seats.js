import presets from "../../../assets/harness-defaults.json";

/**
 * Debate seats are agents. Each is described by what it runs and how it signs in: a CLI agent names its program, optional
 * model and optional stored secret (without one it uses the service machine's own login); a model agent runs on its
 * reusable HTTP model; an HTTP agent names its provider and model.
 */
export function seatOptions({ models = [], secrets = [], agents = [] }, t) {
  const secretName = (id) => secrets.find((item) => item.id === id)?.name;
  return agents.map((agent) => {
    const model = agent.modelProfileId ? models.find((item) => item.id === agent.modelProfileId) : null;
    let detail;
    if (agent.modelProfileId) detail = model ? [t("seat.model", { name: model.name }), model.model].join(" · ") : t("seat.missingModel");
    else if (agent.kind === "rest") detail = [agent.provider, agent.model, agent.secretEnvironmentVariable ? t("seat.environment", { name: agent.secretEnvironmentVariable }) : null].filter(Boolean).join(" · ");
    else detail = [presets[agent.provider]?.name || agent.executable, agent.model || null, agent.credentialId ? t("seat.signedInWith", { secret: secretName(agent.credentialId) || agent.credentialId }) : t("seat.hostLogin")].filter(Boolean).join(" · ");
    return { id: agent.id, name: agent.name, enabled: agent.enabled !== false && model?.enabled !== false, model: detail, warning: Boolean(agent.modelProfileId && !model) };
  });
}
