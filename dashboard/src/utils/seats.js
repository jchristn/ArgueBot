/**
 * Debate seats: reusable models and CLI harnesses carry their sign-in (a stored secret, or the endpoint's
 * authentication bindings), so a debate can always reach its provider. Saved agents follow them. A directly configured
 * CLI agent cannot carry a stored secret: it relies on the service machine's own login, which Docker does not have, so it
 * is flagged with a warning.
 */
export function seatOptions({ models = [], harnesses = [], endpoints = [], secrets = [], agents = [] }, t) {
  const secretName = (id) => secrets.find((item) => item.id === id)?.name;
  const harnessSignIn = (harness) => harness.credentialId
    ? t("seat.signedInWith", { secret: secretName(harness.credentialId) || harness.credentialId })
    : t("seat.hostLogin");
  const describeModel = (model) => {
    if (model.harnessId) {
      const harness = harnesses.find((item) => item.id === model.harnessId);
      return { detail: [model.model, harness?.name, harness ? harnessSignIn(harness) : t("seat.missingHarness")].filter(Boolean).join(" · "), warning: !harness || !harness.credentialId };
    }
    const endpoint = endpoints.find((item) => item.id === model.endpointId);
    const bound = (endpoint?.authBindings || []).some((binding) => binding.credentialId || binding.environmentVariable);
    return { detail: [model.model, endpoint?.name, endpoint ? (bound ? t("seat.endpointAuth") : t("seat.noAuth")) : t("seat.missingEndpoint")].filter(Boolean).join(" · "), warning: !endpoint };
  };
  const seats = [];
  for (const model of models) {
    const { detail, warning } = describeModel(model);
    seats.push({ id: model.id, name: model.name, enabled: model.enabled !== false, kind: t("seat.kind.model"), model: detail, warning });
  }
  for (const harness of harnesses) seats.push({ id: harness.id, name: harness.name, enabled: harness.enabled !== false, kind: t("seat.kind.harness"), model: [harness.provider, harnessSignIn(harness)].join(" · "), warning: !harness.credentialId });
  for (const agent of agents) {
    const model = agent.modelProfileId ? models.find((item) => item.id === agent.modelProfileId) : null;
    const direct = !agent.modelProfileId;
    const detail = !direct ? (model ? describeModel(model).detail : t("seat.missingModel"))
      : agent.kind === "rest" ? [agent.model, agent.provider, agent.secretEnvironmentVariable ? t("seat.environment", { name: agent.secretEnvironmentVariable }) : t("seat.noAuth")].filter(Boolean).join(" · ")
      : [agent.provider || agent.executable, t("seat.hostLogin")].filter(Boolean).join(" · ");
    seats.push({ id: agent.id, name: agent.name, enabled: agent.enabled !== false && model?.enabled !== false, kind: t("seat.kind.agent"), model: detail, warning: direct ? agent.kind !== "rest" : !model });
  }
  return seats;
}
