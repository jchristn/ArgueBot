# ArgueBot: Claude Code vs Codex Debate System

## Concept

An orchestrator process that brokers a structured debate between Claude Code and Codex. The user poses a problem to one agent, and the two argue about the best approach until they reach consensus. The user then extracts a final summary to hand off for implementation.

---

## Architecture

```
                        +------------------+
                        |   Chat Interface |  (terminal UI)
                        +--------+---------+
                                 |
                        +--------v---------+
                        |   Orchestrator   |  (Node.js / Python process)
                        +---+-----------+--+
                            |           |
               +------------v--+   +----v-----------+
               | Claude Code   |   | Codex (codex)  |
               | (claude CLI)  |   |                |
               +---------------+   +----------------+
```

### Three layers:

1. **Chat Interface** -- what the user sees and types into
2. **Orchestrator** -- manages turns, context, consensus detection, and mode transitions
3. **Agent Subprocesses** -- Claude Code and Codex, driven via their CLIs in non-interactive mode

---

## How Each Agent Is Driven

### Claude Code

```bash
claude -p "your prompt here" --dangerously-skip-permissions
```

`-p` (print mode) accepts a prompt on the CLI, runs non-interactively, and prints the response to stdout. No interactive session needed.

### Codex

```bash
codex --yolo -q "your prompt here"
```

`-q` (quiet/non-interactive) sends a single prompt and returns the result. `--yolo` disables confirmation prompts. Verify exact flags -- Codex's CLI may differ; the key requirement is a non-interactive single-shot mode that writes to stdout.

### Alternative: SDK/API approach

If CLI spawning proves fragile (buffering, encoding, timeouts), both tools likely expose programmatic interfaces:
- Claude Code: the `@anthropic-ai/claude-code` SDK (`claude.query()`)
- Codex: OpenAI Codex API or the `codex` npm package

The plan should support swapping the subprocess driver for an SDK driver without changing the orchestrator logic.

---

## Orchestrator Design

### State Machine

```
USER_INPUT --> AGENT_A_TURN --> DISPLAY_A --> AGENT_B_TURN --> DISPLAY_B --> CONSENSUS_CHECK
                  ^                                                              |
                  |                        no consensus                          |
                  +---> HUMAN_INTERVENTION (optional, any time) <---+            |
                  |              |                                  |            |
                  |              v                                  |            |
                  +---- inject user note into transcript -----------+            |
                                                                                |
                                                                       consensus reached
                                                                                |
                                                                                v
                                                                       USER_FINAL_QUERY
                                                                                |
                                                                                v
                                                                              DONE
```

### Display & Intervention Model

**Every turn is displayed in real-time.** After each agent responds, its full response is rendered to the chat UI immediately (color-coded by speaker). The user watches the debate unfold turn by turn.

**Between any two turns, the user can intervene.** The orchestrator briefly pauses after displaying each agent's response (a short input window, e.g., 5-10 seconds with a visible countdown or a "Press Enter to continue / type to intervene" prompt). This gives the user a chance to:

| Intervention | Effect |
|-------------|--------|
| *Press Enter / wait* | Debate continues to next turn normally |
| Type a message | User's message is injected into the transcript as a "moderator note" that both agents see in subsequent turns |
| `/steer "focus on performance"` | Appends a directive to the next agent's prompt without appearing as a debate argument |
| `/pause` | Halts the debate; user can resume with `/resume` |
| `/stop` | Ends the debate early, transitions to `USER_FINAL_QUERY` |
| `/extend N` | Adds N more rounds to the max |
| `/reset` | Scraps the debate, starts over with a new prompt |

**If no intervention occurs within the timeout window, the next turn fires automatically.** This keeps the debate flowing without requiring the user to hit Enter every time, while still giving them a chance to jump in.

**Implementation detail:** The input window is non-blocking. While waiting for user input, a countdown timer is visible. The UI accepts keystrokes at any point -- if the user starts typing, the countdown pauses and waits for them to finish (Enter to submit). If the countdown expires, the next turn proceeds.

### Key Orchestrator Responsibilities

1. **Turn management** -- alternate between agents, injecting the full debate history each turn
2. **Context framing** -- wrap each agent's prompt so it knows:
   - The original user question
   - The full debate transcript so far
   - Its role ("You are arguing FOR/AGAINST this approach" or simply "respond to the other agent's position")
   - Instructions to state agreement explicitly if convinced
3. **Consensus detection** -- after each round, check if the responding agent explicitly agrees with the other (keyword/phrase matching or a lightweight LLM classification call)
4. **Max rounds** -- cap debate at N rounds (default: 6 exchanges, i.e. 3 rounds each) to avoid infinite loops
5. **Mode transition** -- once consensus is detected (or max rounds hit), switch to "user query" mode where the user can ask follow-up questions against the full transcript

### Prompt Templates

**Initial prompt (to Agent A):**
```
You are participating in a technical debate. A user has asked:

"{user_prompt}"

Propose your approach to solving this. Be specific and opinionated.
Lay out your reasoning, trade-offs, and implementation strategy.
```

**Rebuttal prompt (to Agent B, and subsequent turns):**
```
You are participating in a technical debate. The original question is:

"{user_prompt}"

Here is the debate so far:
{transcript}

Respond to the other participant's most recent argument.
- If you disagree, explain why and propose an alternative.
- If you agree, say "I AGREE" explicitly and summarize the consensus position.
Be specific. Cite concrete technical reasons.
```

**Consensus/summary prompt (to either agent, post-consensus):**
```
The debate has concluded. Here is the full transcript:

{transcript}

The user asks: "{user_follow_up}"

Respond based on the consensus reached in the debate.
```

---

## Chat Interface

### Option A: Terminal UI (recommended for v1)

Use a library like **Ink** (React for CLI, Node.js) or **rich** / **textual** (Python) to build a terminal chat interface that shows:

- A scrolling transcript with color-coded speakers:
  - **Green**: User
  - **Blue**: Claude Code
  - **Orange/Yellow**: Codex
  - **Gray**: System (round counter, consensus status)
- An input bar at the bottom
- A status line showing: current round, whose turn it is, whether consensus has been reached

### Option B: Web UI (if you want something fancier later)

A simple localhost web app (Next.js, Vite, etc.) with a chat panel. The orchestrator exposes a WebSocket or SSE endpoint. This is overkill for v1.

### User Interactions

| State | User Can Do |
|-------|------------|
| `USER_INPUT` | Type the initial prompt, choose which agent goes first |
| `DEBATING` | Watch the debate, optionally intervene ("stop", "focus on X") |
| `CONSENSUS` | Ask follow-up questions, request summary, copy output |
| `DONE` | Start a new debate or exit |

---

## Implementation Plan

### Phase 1: Core Orchestrator (get it working)

1. **Scaffold the project** -- Node.js (TypeScript) or Python project, your choice
2. **Agent driver module** -- a function `callAgent(agent: "claude" | "codex", prompt: string): Promise<string>` that spawns the CLI subprocess, captures stdout, handles timeouts (60s default), and returns the response
3. **Debate loop** -- implement the state machine: accept user input, alternate agents, build transcript, check for consensus after each response
4. **Consensus detection** -- v1: simple regex/keyword check for phrases like "I agree", "I concur", "consensus", "you're right". v2: ask a lightweight model to classify
5. **Simple CLI interface** -- just stdin/stdout with colored output (chalk/ansi). No fancy UI yet

### Phase 2: Chat Interface

6. **Terminal UI** -- build the Ink or Textual interface described above
7. **User controls** -- let the user intervene mid-debate, adjust max rounds, pick which agent starts
8. **Transcript export** -- copy full debate to clipboard or write to a file (markdown formatted) for handoff to another agent

### Phase 3: Polish

9. **Streaming** -- if the CLIs support streaming output, pipe it to the UI in real-time instead of waiting for full responses
10. **Context windowing** -- if debates get long and exceed token limits, summarize earlier rounds before injecting into the next prompt
11. **Configurable personas** -- let the user set the "stance" for each agent (e.g., "argue for simplicity" vs "argue for correctness")
12. **SDK driver swap** -- replace subprocess spawning with SDK calls for reliability

---

## Key Technical Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| CLI output includes spinner/progress artifacts | Strip ANSI codes and non-content output from captured stdout; use `--output-format text` flags if available |
| Agents never reach consensus | Max round cap + forced summary at the end ("summarize the key disagreements") |
| Agents immediately agree (boring debate) | Prompt engineering: instruct the second agent to play devil's advocate for at least 2 rounds |
| Token limits exceeded with long transcripts | Sliding window: summarize rounds older than N before injecting into prompt |
| Subprocess hangs or crashes | Timeout + retry with exponential backoff; surface error to user |
| Codex CLI flags differ from assumed | Test `codex --help` and adjust; the driver module abstracts this away |

---

## Tech Stack Recommendation

| Component | Recommendation | Why |
|-----------|---------------|-----|
| Language | TypeScript (Node.js) | Both CLIs are Node-friendly; Ink gives great terminal UIs |
| Subprocess | `execa` | Better than `child_process` -- handles encoding, buffering, timeouts |
| Terminal UI | `Ink` (React for CLI) | Declarative, component-based terminal UI; color-coded chat is easy |
| Transcript format | Markdown | Easy to export, easy to paste into another agent |

---

## Example Session

```
$ npx arguebot

ArgueBot v0.1 -- Claude Code vs Codex Debate
─────────────────────────────────────────────

Choose who goes first: [C]laude / Code[x]  > C

You > Should we use a monorepo or polyrepo for a microservices
      architecture with 12 services and a shared component library?

[Round 1/3]

Claude Code > I'd advocate for a monorepo using Turborepo or Nx...
              [detailed argument]

  ⏳ Continue in 8s... (type to intervene, Enter to skip)  _

Codex       > I'd push back on the monorepo approach. At 12 services...
              [detailed counterargument]

  ⏳ Continue in 8s... (type to intervene, Enter to skip)  _
You > Both of you are ignoring that 4 of the 12 services are in Go,
      not TypeScript. Factor that in.

  📌 Moderator note injected into transcript.

[Round 2/3]

Claude Code > Good point about the Go services. This actually
              strengthens the polyrepo case for those 4, but we could
              use a hybrid approach...
              [rebuttal incorporating user's note]

  ⏳ Continue in 8s... (type to intervene, Enter to skip)  _

Codex       > I AGREE with the hybrid position. Monorepo for the 8 TS
              services via Turborepo, separate repos for the Go
              services with shared proto definitions...
              [consensus statement]

✓ Consensus reached after 2 rounds.

You > Summarize the consensus as a bullet-point implementation plan
      I can hand to an engineer.

Claude Code > Based on our debate, here's the consensus plan:
              - Monorepo (Turborepo) for the 8 TypeScript services...
              - Separate repos for the 4 Go services...
              - Shared proto/API contract repo...
              [structured summary]

[Copied to clipboard]
```

---

## What This Plan Does NOT Cover

- **File system access during debate** -- the agents are run in prompt-only mode; they don't modify files during the debate. Implementation happens later, by the user handing the consensus output to a separate agent session.
- **Multi-agent (3+) debates** -- v1 is strictly two agents. Could extend later.
- **Persistent memory across debates** -- each debate is stateless. Could add later.
