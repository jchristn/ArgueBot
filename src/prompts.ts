import type { AgentName, DebateTurn } from "./types.js";

function agentLabel(agent: AgentName): string {
  switch (agent) {
    case "claude":
      return "Claude Code";
    case "codex":
      return "Codex";
    case "gemini":
      return "Gemini";
  }
}

/**
 * Opponents may be a single agent (classic two-way debate) or several
 * agents (panel debate). Labels are joined into readable prose.
 */
function describeOpponents(opponents: AgentName | AgentName[]): string {
  const labels = (Array.isArray(opponents) ? opponents : [opponents]).map(agentLabel);
  if (labels.length <= 1) {
    return labels[0] ?? "another agent";
  }
  return `${labels.slice(0, -1).join(", ")} and ${labels[labels.length - 1]}`;
}

function isPanel(opponents: AgentName | AgentName[]): boolean {
  return Array.isArray(opponents) && opponents.length > 1;
}

function formatTranscript(transcript: DebateTurn[]): string {
  return transcript
    .map((t) => {
      const label =
        t.speaker === "user"
          ? "[Moderator (User)]"
          : `[${agentLabel(t.speaker as AgentName)}]`;
      return `${label}\n${t.content}`;
    })
    .join("\n\n---\n\n");
}

const CONSENSUS_INSTRUCTIONS = `
CONSENSUS RULE: When -- and ONLY when -- you genuinely believe you and your opponent have reached full agreement on the substantive position, write the exact phrase "WE HAVE CONSENSUS" on its own line, followed by a clear summary of the agreed position. Do NOT use this phrase if you still have unresolved objections. Partial agreement ("I agree on one point but...") is NOT consensus. The debate continues until one of you writes "WE HAVE CONSENSUS" and means it.`.trim();

const PANEL_CONSENSUS_INSTRUCTIONS = `
CONSENSUS RULE: When -- and ONLY when -- you genuinely believe EVERY participant in this debate has reached full agreement on the substantive position, write the exact phrase "WE HAVE CONSENSUS" on its own line, followed by a clear summary of the agreed position. Do NOT use this phrase if you or any other participant still has unresolved objections. Partial agreement ("I agree on one point but...") is NOT consensus. The debate continues until a participant writes "WE HAVE CONSENSUS" and means it.`.trim();

function consensusInstructions(opponents: AgentName | AgentName[]): string {
  return isPanel(opponents) ? PANEL_CONSENSUS_INSTRUCTIONS : CONSENSUS_INSTRUCTIONS;
}

export function buildOpeningPrompt(
  userPrompt: string,
  agent: AgentName,
  opponent: AgentName | AgentName[],
): string {
  return `You are ${agentLabel(agent)}, participating in a structured technical debate with ${describeOpponents(opponent)}.

A user has asked the following question:

"${userPrompt}"

You are giving the OPENING POSITION. Your job:
1. Propose a specific, opinionated approach to solving this.
2. State your assumptions explicitly.
3. Lay out your reasoning, trade-offs, and implementation strategy.
4. Identify risks and unknowns.

Be concrete and direct. Do not hedge excessively. Take a clear position.

${consensusInstructions(opponent)}`;
}

export function buildRebuttalPrompt(
  userPrompt: string,
  transcript: DebateTurn[],
  agent: AgentName,
  opponent: AgentName | AgentName[],
  steerDirective: string | null,
): string {
  const formatted = formatTranscript(transcript);

  const steer = steerDirective
    ? `\n\nIMPORTANT DIRECTIVE FROM THE USER: ${steerDirective}`
    : "";

  const respondTo = isPanel(opponent)
    ? "the most recent arguments from the other participants"
    : `${agentLabel(opponent as AgentName)}'s most recent argument`;

  return `You are ${agentLabel(agent)}, participating in a structured technical debate with ${describeOpponents(opponent)}.

The original question is:
"${userPrompt}"

Here is the debate so far:

${formatted}

Respond to ${respondTo}. If the moderator (user) has added notes, take them into account.
- If you disagree, explain WHY with concrete technical reasons and propose an alternative.
- If you partially agree, acknowledge the strong points but push back on weaknesses.
- Do not repeat arguments already made. Add new information or concede points where warranted.
- Concede individual points freely when they are correct -- but do not declare full consensus until you truly have no remaining objections.${steer}

Be specific. Cite concrete technical reasons.

${consensusInstructions(opponent)}`;
}

export function buildFinalQueryPrompt(
  userPrompt: string,
  transcript: DebateTurn[],
  userFollowUp: string,
  agent: AgentName,
): string {
  const formatted = formatTranscript(transcript);

  return `You are ${agentLabel(agent)}. A technical debate has concluded. Here is the full context:

Original question: "${userPrompt}"

Full debate transcript:

${formatted}

---

The user now asks: "${userFollowUp}"

Respond helpfully based on the debate above. If consensus was reached, present the agreed position. If not, present the key options with trade-offs. Be concise and actionable.`;
}
