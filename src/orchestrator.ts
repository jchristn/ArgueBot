import { callAgent, validateAgentSetup } from "./agents.js";
import { checkConsensus } from "./consensus.js";
import { buildFinalQueryPrompt, buildOpeningPrompt, buildRebuttalPrompt } from "./prompts.js";
import { saveTranscript } from "./transcript.js";
import type { AgentName, DebateConfig, DebateSession, DebateTurn } from "./types.js";
import { DEFAULT_CONFIG } from "./types.js";
import * as ui from "./ui.js";

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

function opponentFor(config: DebateConfig, agent: AgentName): AgentName {
  return agent === config.firstAgent ? config.secondAgent : config.firstAgent;
}

function isAgentChoice(input: string): input is AgentName {
  return input === "claude" || input === "codex" || input === "gemini";
}

function formatAgentOption(agent: AgentName): string {
  switch (agent) {
    case "claude":
      return "[c]laude";
    case "codex":
      return "code[x]";
    case "gemini":
      return "[g]emini";
  }
}

async function chooseAgent(
  prompt: string,
  defaultAgent: AgentName,
  allowedAgents: AgentName[],
): Promise<AgentName> {
  const answer = await ui.askUser(prompt);
  const normalized = answer.trim().toLowerCase();

  if (normalized.length === 0) {
    return defaultAgent;
  }

  if (normalized === "c" && allowedAgents.includes("claude")) {
    return "claude";
  }
  if (normalized === "x" && allowedAgents.includes("codex")) {
    return "codex";
  }
  if (normalized === "g" && allowedAgents.includes("gemini")) {
    return "gemini";
  }
  if (isAgentChoice(normalized) && allowedAgents.includes(normalized)) {
    return normalized;
  }

  ui.printSystemMessage(`Unrecognized choice "${answer}". Using ${agentLabel(defaultAgent)}.`);
  return defaultAgent;
}

function createSession(userPrompt: string, config: DebateConfig): DebateSession {
  return {
    id: Date.now().toString(36),
    userPrompt,
    config,
    state: "user_input",
    transcript: [],
    currentRound: 1,
    currentSpeaker: config.firstAgent,
    steerDirective: null,
    consensusReached: false,
  };
}

function addTurn(
  session: DebateSession,
  speaker: AgentName | "user",
  content: string,
  type: DebateTurn["type"],
): DebateTurn {
  const turn: DebateTurn = {
    round: session.currentRound,
    speaker,
    content,
    timestamp: new Date(),
    type,
  };
  session.transcript.push(turn);
  return turn;
}

function parseIntervention(
  input: string,
): { action: "continue" | "steer" | "pause" | "stop" | "extend" | "reset" | "help" | "inject"; value?: string } {
  if (input.startsWith("/steer ")) {
    return { action: "steer", value: input.slice(7).replace(/^["']|["']$/g, "") };
  }
  if (input === "/pause") return { action: "pause" };
  if (input === "/stop") return { action: "stop" };
  if (input === "/reset") return { action: "reset" };
  if (input === "/help") return { action: "help" };
  if (input.startsWith("/extend")) {
    const n = parseInt(input.split(" ")[1] ?? "2", 10);
    return { action: "extend", value: String(isNaN(n) ? 2 : n) };
  }
  return { action: "inject", value: input };
}

async function runAgentTurn(
  session: DebateSession,
  agent: AgentName,
  isOpening: boolean,
): Promise<boolean> {
  session.state = "agent_turn";

  let prompt: string;
  if (isOpening) {
    prompt = buildOpeningPrompt(session.userPrompt, agent, opponentFor(session.config, agent));
  } else {
    prompt = buildRebuttalPrompt(
      session.userPrompt,
      session.transcript,
      agent,
      opponentFor(session.config, agent),
      session.steerDirective,
    );
    session.steerDirective = null;
  }

  // Print the agent header, then stream content directly to stdout
  ui.printAgentHeader(agent);

  const result = await callAgent(agent, prompt, {
    timeoutMs: session.config.agentTimeoutMs,
    yoloMode: session.config.yoloMode,
    onChunk: (chunk) => {
      process.stdout.write(chunk);
    },
  });

  if (result.error) {
    console.log(); // newline after any partial output
    ui.printError(result.error);
    ui.printSystemMessage(`Skipping ${agent}'s turn due to error.`);
    return false;
  }

  // End the streamed block with a newline
  console.log();
  ui.printSystemMessage(`(${(result.durationMs / 1000).toFixed(1)}s)`);

  // Record the turn in the transcript
  const turnType = isOpening ? "opening" : "rebuttal";
  addTurn(session, agent, result.content, turnType);

  return true;
}

async function handleInterventionWindow(session: DebateSession): Promise<"continue" | "stop" | "reset"> {
  session.state = "waiting_intervention";
  const input = await ui.waitForIntervention(session.config.interventionTimeoutMs);

  if (input === null) {
    return "continue";
  }

  const parsed = parseIntervention(input);

  switch (parsed.action) {
    case "steer":
      session.steerDirective = parsed.value!;
      ui.printSystemMessage(`Steering directive set: "${parsed.value}"`);
      return "continue";

    case "pause": {
      ui.printSystemMessage("Debate paused. Type /resume to continue.");
      while (true) {
        const resumeInput = await ui.askUser("(paused) > ");
        if (resumeInput === "/resume") {
          ui.printSystemMessage("Debate resumed.");
          return "continue";
        }
        if (resumeInput === "/stop") return "stop";
        if (resumeInput === "/reset") return "reset";
        ui.printSystemMessage("Type /resume, /stop, or /reset.");
      }
    }

    case "stop":
      return "stop";

    case "reset":
      return "reset";

    case "extend":
      session.config.maxRounds += parseInt(parsed.value!, 10);
      ui.printSystemMessage(`Max rounds extended to ${session.config.maxRounds}.`);
      return "continue";

    case "help":
      ui.printHelp();
      return handleInterventionWindow(session);

    case "inject":
      addTurn(session, "user", parsed.value!, "moderator");
      ui.printSystemMessage("Moderator note injected into transcript.");
      return "continue";

    default:
      return "continue";
  }
}

export async function runDebate(configOverrides: Partial<DebateConfig> = {}): Promise<void> {
  const config = { ...DEFAULT_CONFIG, ...configOverrides };

  ui.printBanner();

  if (config.firstAgent === config.secondAgent) {
    config.secondAgent = config.firstAgent === "claude" ? "codex" : "claude";
  }

  // Choose first agent
  if (configOverrides.firstAgent === undefined) {
    config.firstAgent = await chooseAgent(
      `Choose who goes first [default ${config.firstAgent}]: [c]laude / code[x] / [g]emini > `,
      config.firstAgent,
      ["claude", "codex", "gemini"],
    );
  }

  // Choose second agent
  const secondDefault =
    config.secondAgent !== config.firstAgent
      ? config.secondAgent
      : (["claude", "codex", "gemini"].find((agent) => agent !== config.firstAgent) as AgentName);
  if (configOverrides.secondAgent === undefined) {
    config.secondAgent = await chooseAgent(
      `Choose the opposing agent [default ${secondDefault}]: [c]laude / code[x] / [g]emini > `,
      secondDefault,
      (["claude", "codex", "gemini"] as AgentName[]).filter((agent) => agent !== config.firstAgent),
    );
  } else if (config.secondAgent === config.firstAgent) {
    ui.printSystemMessage("Second agent matched the first agent. Falling back to a different debater.");
    config.secondAgent = secondDefault;
  }

  // Choose max rounds
  const roundsInput = await ui.askUser(
    `Max rounds (default ${config.maxRounds}) > `,
  );
  if (roundsInput) {
    const n = parseInt(roundsInput, 10);
    if (!isNaN(n) && n > 0) {
      config.maxRounds = n;
    }
  }

  // Choose summary agent
  const summaryDefault =
    config.summaryAgent === config.firstAgent || config.summaryAgent === config.secondAgent
      ? config.summaryAgent
      : config.firstAgent;
  if (configOverrides.summaryAgent === undefined) {
    config.summaryAgent = await chooseAgent(
      `Which agent for summary [default ${summaryDefault}]: ${formatAgentOption(config.firstAgent)} / ${formatAgentOption(config.secondAgent)} > `,
      summaryDefault,
      [config.firstAgent, config.secondAgent],
    );
  } else if (config.summaryAgent !== config.firstAgent && config.summaryAgent !== config.secondAgent) {
    ui.printSystemMessage("Summary agent was not one of the selected debaters. Falling back to the first agent.");
    config.summaryAgent = config.firstAgent;
  }

  const setupErrors = [...new Set([config.firstAgent, config.secondAgent, config.summaryAgent])]
    .map((agent) => validateAgentSetup(agent))
    .filter((message): message is string => message !== null);

  if (setupErrors.length > 0) {
    for (const message of setupErrors) {
      ui.printError(message);
    }
    ui.printSystemMessage("Fix the missing configuration and run ArgueBot again.");
    return;
  }

  console.log();
  ui.printSystemMessage("");
  ui.printSystemMessage(
    "Enter the prompt you wish to send to the first agent to begin. ENTER = newline, ESC then ENTER = submit.",
  );
  ui.printSystemMessage("");
  const userPrompt = await ui.askUser("> ", {
    multiline: true,
  });
  if (!userPrompt) {
    ui.printSystemMessage("No prompt provided. Exiting.");
    return;
  }

  const session = createSession(userPrompt, config);
  console.log();

  // === DEBATE LOOP ===

  let debateActive = true;

  while (debateActive) {
    ui.printRoundHeader(session.currentRound, session.config.maxRounds);

    // Agent A turn
    const agentA = session.currentRound === 1 ? config.firstAgent : session.currentSpeaker;
    const isOpening = session.currentRound === 1 && agentA === config.firstAgent && session.transcript.length === 0;
    const turnAOk = await runAgentTurn(session, agentA, isOpening);

    if (turnAOk) {
      const consensusA = checkConsensus(session.transcript[session.transcript.length - 1].content);
      if (consensusA.reached && !isOpening) {
        session.consensusReached = true;
        session.state = "consensus_reached";
        ui.printConsensus(true);
        break;
      }
    }

    const actionA = await handleInterventionWindow(session);
    if (actionA === "stop") {
      debateActive = false;
      break;
    }
    if (actionA === "reset") {
      ui.printSystemMessage("Resetting debate...");
      return runDebate(configOverrides);
    }

    // Agent B turn
    const agentB = opponentFor(config, agentA);
    const turnBOk = await runAgentTurn(session, agentB, false);

    if (turnBOk) {
      const consensusB = checkConsensus(session.transcript[session.transcript.length - 1].content);
      if (consensusB.reached) {
        session.consensusReached = true;
        session.state = "consensus_reached";
        ui.printConsensus(true);
        break;
      }
    }

    const actionB = await handleInterventionWindow(session);
    if (actionB === "stop") {
      debateActive = false;
      break;
    }
    if (actionB === "reset") {
      ui.printSystemMessage("Resetting debate...");
      return runDebate(configOverrides);
    }

    session.currentRound++;
    if (session.currentRound > session.config.maxRounds) {
      session.state = "max_rounds_reached";
      ui.printConsensus(false);
      debateActive = false;
    }

    session.currentSpeaker = agentA;
  }

  // === FINAL Q&A LOOP ===

  session.state = "user_final_query";
  ui.printSystemMessage("You can now ask follow-up questions. Type /done to finish, /save to export transcript.");
  console.log();

  while (true) {
    const followUp = await ui.askUser("Follow-up > ", {
      multiline: true,
    });

    if (!followUp || followUp === "/done") {
      break;
    }

    if (followUp === "/save") {
      const filepath = saveTranscript(
        session.userPrompt,
        session.transcript,
        session.consensusReached,
      );
      ui.printSystemMessage(`Transcript saved to: ${filepath}`);
      continue;
    }

    if (followUp === "/transcript") {
      ui.printTranscriptSummary(session.transcript);
      continue;
    }

    const respondingAgent = config.summaryAgent;
    ui.printAgentHeader(respondingAgent);

    const prompt = buildFinalQueryPrompt(
      session.userPrompt,
      session.transcript,
      followUp,
      respondingAgent,
    );

    const result = await callAgent(respondingAgent, prompt, {
      timeoutMs: session.config.agentTimeoutMs,
      yoloMode: session.config.yoloMode,
      onChunk: (chunk) => {
        process.stdout.write(chunk);
      },
    });

    if (result.error) {
      console.log();
      ui.printError(result.error);
    } else {
      console.log();
      addTurn(session, respondingAgent, result.content, "final");
    }
  }

  // Auto-save on exit
  const filepath = saveTranscript(
    session.userPrompt,
    session.transcript,
    session.consensusReached,
  );
  ui.printSystemMessage(`Transcript saved to: ${filepath}`);

  session.state = "done";
  console.log();
  ui.printSystemMessage("Debate complete. Goodbye!");
  console.log();
}
