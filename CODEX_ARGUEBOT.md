# Codex Arguebot Plan

## Goal

Create a chat-based scaffolding layer that sits in front of two CLI agents:

- `claude --dangerously-skip-permissions`
- `codex --yolo`

The user sends one prompt to either agent, the system lets the two agents debate the right approach, and once they converge the user can ask for a final artifact such as a summary, recommendation, or handoff package for implementation elsewhere.

This document describes the approach only. It does not specify code.

## Product Shape

The product is a thin orchestrator with a chat UI and two agent adapters.

Core idea:

1. User opens a chat session.
2. User chooses the starting agent, or the UI infers it from the active pane.
3. User submits a prompt once.
4. The orchestrator forwards the prompt to the chosen agent as the opening position.
5. The other agent receives that position and responds with critique, alternatives, or approval.
6. The orchestrator continues the exchange turn by turn under a strict protocol.
7. When a stop condition is reached, the orchestrator presents:
   - consensus result, or
   - unresolved disagreement with explicit tradeoffs.
8. User asks one final follow-up such as “summarize the agreed plan” or “produce an implementation brief.”

## Primary Use Cases

- Compare architectural approaches before implementation.
- Stress-test a plan with an adversarial review loop.
- Force one model to justify assumptions to another.
- Produce a higher-confidence handoff artifact for a separate implementation agent.

## Non-Goals

- Fully autonomous coding and file modification.
- Unbounded multi-agent chat with no stop criteria.
- Rich memory across many sessions in the first version.
- Perfect consensus. “Structured disagreement” is an acceptable end state.

## Recommended System Architecture

Use four layers.

### 1. Chat UI

Responsibilities:

- Show the user conversation.
- Let the user choose starting agent: Claude or Codex.
- Stream agent messages in sequence.
- Show debate state:
  - round number
  - current speaker
  - debate phase
  - convergence status
- Expose explicit controls:
  - start debate
  - pause
  - resume
  - stop and summarize
  - ask final question

Recommended UX model:

- One main user chat pane.
- One collapsible “agent debate transcript” pane.
- One “consensus / open issues” panel that updates live.

### 2. Orchestrator

This is the core component.

Responsibilities:

- Manage session state.
- Launch and supervise both CLI processes.
- Route messages between the user and the selected agent.
- Translate raw outputs into structured debate turns.
- Enforce debate protocol and stop conditions.
- Detect failure states and retry or terminate safely.

This layer should be deterministic where possible. Do not let either agent decide the debate mechanics.

### 3. Agent Adapters

Create one adapter per CLI:

- Claude adapter
- Codex adapter

Responsibilities:

- Start the CLI process with the desired arguments.
- Inject system instructions for debate behavior.
- Feed turns into stdin or the supported input mechanism.
- Capture stdout/stderr.
- Normalize each response into a common internal format.

Important constraint:

Each CLI likely has different interaction semantics, streaming behavior, prompt formatting, and session persistence. The adapters isolate those differences so the orchestrator sees the same contract from both.

### 4. State and Transcript Store

Store:

- original user prompt
- selected starting agent
- debate rounds
- intermediate summaries
- consensus items
- unresolved disagreements
- final user follow-up question
- final artifact

First version can use simple local persistence. A database is optional unless you want multi-session retrieval and analytics immediately.

## Core Conversation Model

Treat this as a structured debate, not a free-form group chat.

Each debate turn should carry:

- speaker
- intended audience
- round number
- claim
- reasoning
- critique of the previous turn
- confidence level
- proposed next step
- consensus markers

The orchestrator should require agents to answer in a constrained schema, even if the UI renders it naturally. This is the key to making the system manageable.

Example internal phases:

1. Opening proposal
2. Critique
3. Revision
4. Risk review
5. Convergence check
6. Final synthesis

## Debate Protocol

Use a fixed protocol in the first version.

### Round 0: User Prompt Intake

The user submits one prompt to one chosen agent.

The orchestrator appends hidden instructions such as:

- answer as the opening position
- make assumptions explicit
- propose one recommended approach
- list risks and unknowns
- keep the response bounded

### Round 1: Counterparty Critique

The second agent receives:

- the original user prompt
- the first agent’s structured answer

It must:

- identify weak assumptions
- propose improvements or reject the approach
- state points of agreement
- ask the minimum needed clarifying questions

### Round 2+: Directed Rebuttal

The orchestrator alternates turns with a strict budget, for example:

- 4 to 8 total turns
- token cap per turn
- no repeated arguments
- each turn must explicitly update consensus state

### Final Debate Step

The orchestrator asks both agents one final question:

- “State the agreed solution if consensus exists.”
- “If consensus does not exist, state the best two options and the decision criteria.”

Then the orchestrator synthesizes a stable end-state for the user.

## Consensus Model

Do not define consensus as “both agents stopped talking.”

Define consensus explicitly. For example:

- both agents endorse the same primary approach, or
- one agent concedes after critique, or
- both agents agree on a ranked shortlist with clear tradeoffs.

Consensus object should contain:

- recommended approach
- rationale
- assumptions
- risks
- unresolved items
- confidence

If no consensus is reached, produce a “decision brief” instead.

## User Experience Flow

### Initial Session

1. User opens chat UI.
2. User selects `Claude` or `Codex` as the starting agent.
3. User enters prompt.
4. UI shows:
   - “Opening position from Codex”
   - “Critique from Claude”
   - subsequent rounds as the debate proceeds
5. A side panel updates:
   - current agreement points
   - live disagreements
   - likely final recommendation

### End of Debate

When the stop condition triggers, the UI should present one of two outcomes:

- `Consensus reached`
- `No consensus; tradeoff summary prepared`

The user then gets a simple input box:

- “Ask a final question about the result”

Examples:

- “Summarize the agreed architecture.”
- “Turn this into an implementation brief.”
- “List open risks before coding.”
- “Give me the strongest argument against the winning approach.”

### Why this UX works

- The user only authors one initial prompt.
- The agent-to-agent interaction is visible but contained.
- The final user interaction happens after the debate, not during it.
- The product behaves like a structured review meeting, not a chaotic chatbot swarm.

## Required Internal Controls

### Turn Budget

Hard-limit the number of rounds. Otherwise the agents will loop, restate, or drift.

Recommended starting point:

- 1 opening turn
- 1 critique turn
- 2 rebuttal turns
- 1 convergence turn

### Response Budget

Constrain each turn with:

- max tokens
- numbered points only
- explicit agreement/disagreement sections

This improves parsing and keeps the UX readable.

### Topic Locking

Do not allow either agent to change the objective midstream. The orchestrator should reject drift such as:

- introducing unrelated implementation detail
- rewriting the user’s goal
- escalating scope

### Clarification Gate

If an agent asks a clarifying question, the orchestrator should decide:

- can the debate continue with assumptions?
- must the user answer?

Most of the time, continue with stated assumptions. Only interrupt the user if the ambiguity materially changes the recommended approach.

## CLI Integration Considerations

This is where most real complexity will live.

### Process Management

Need to handle:

- launching each CLI process
- session lifecycle
- timeouts
- cancellation
- partial output streaming
- process crashes
- retries

### Prompt Injection Strategy

Each agent needs a stable hidden instruction set covering:

- debate role
- concise output format
- obligation to critique weak reasoning
- obligation to acknowledge valid counterarguments
- obligation to update consensus state every turn

This hidden prompt should be controlled only by the orchestrator.

### Output Normalization

Raw model output is unreliable for orchestration. Normalize into a shared structure before routing it onward.

Even if the models return plain text, the adapter should parse it into fields such as:

- `position`
- `criticisms`
- `agreements`
- `revised_recommendation`
- `confidence`

### Session Isolation

Each debate session should be isolated. Do not leak previous debate history unless deliberately included as context.

## Failure Modes to Design For

### 1. Infinite Agreement Loops

Both agents may repeatedly say the same thing in different words.

Mitigation:

- semantic similarity checks
- repeated-point detection
- max rounds

### 2. Performative Disagreement

One model may invent weak objections just to keep the debate going.

Mitigation:

- require evidence or explicit reasoning for objections
- score novelty of critique
- terminate when objections are low-signal

### 3. Prompt Drift

Agents may start discussing implementation details when the user asked for architecture only.

Mitigation:

- phase-specific prompts
- topic lock in orchestrator
- stop and restate scope when drift is detected

### 4. CLI Instability

One CLI may hang, crash, or change its output format.

Mitigation:

- adapter abstraction
- timeout and restart policy
- degraded mode where the remaining agent produces a solo analysis with a warning

### 5. Fake Consensus

Agents may superficially agree without resolving the core issue.

Mitigation:

- consensus requires explicit agreement on:
  - recommendation
  - assumptions
  - top risk
- orchestrator checks the fields, not the tone

## Security and Operational Risks

Your proposed launch flags are intentionally permissive:

- `claude --dangerously-skip-permissions`
- `codex --yolo`

That makes this suitable only for a controlled local environment.

Concerns:

- either CLI may execute tools or inspect files depending on its defaults
- debate prompts may accidentally trigger actions instead of analysis
- a malicious or poorly scoped prompt could cause dangerous behavior

Recommended guardrails:

- first version should be analysis-only
- disable file mutation and command execution if the CLIs support that separation
- run inside an isolated workspace
- clearly label session mode as `read-only debate` vs `execution-enabled`

If true execution is needed later, treat it as a separate product mode.

## Suggested First Milestone

Build the minimum useful version around a narrow contract.

### V1 Scope

- One chat UI
- Manual choice of starting agent
- One debate topic at a time
- Fixed 4- to 6-turn protocol
- Plain text rendering with structured sections
- Final summarization question supported
- Local transcript persistence

### V1 Output Types

Support only:

- recommended approach
- tradeoff summary
- implementation brief
- risk summary

Avoid broader artifact generation initially.

## Suggested Orchestrator Logic

High-level sequence:

1. Accept user prompt and selected starting agent.
2. Generate opening instruction package for that agent.
3. Parse opening answer into structured turn.
4. Pass structured turn plus original prompt to the other agent.
5. Alternate turns while:
   - round budget remains
   - new information is still appearing
   - no clear consensus object exists
6. Trigger convergence check.
7. Build final debate result:
   - consensus report, or
   - decision brief
8. Wait for user final question.
9. Ask one or both agents for the final artifact, or synthesize it in the orchestrator.

Important design choice:

The orchestrator should own the final state. Do not let one model’s final answer become the source of truth without validation against the recorded debate state.

## Prompt Design Requirements

Each agent needs role-specific instructions.

Opening agent prompt should emphasize:

- propose a concrete approach
- state assumptions
- avoid rambling

Responding agent prompt should emphasize:

- critique, not just restate
- identify the strongest and weakest parts of the proposal
- concede good points
- propose a better version if possible

Convergence prompt should emphasize:

- stop advocating
- summarize agreed facts
- identify irreducible disagreements
- rank options

## Observability

You will want instrumentation early because multi-agent flows are hard to debug.

Track:

- per-turn latency
- token usage if available
- number of rounds
- cause of termination
- parser failures
- retries
- consensus vs no-consensus rate

Keep raw transcripts for debugging, but also keep normalized turn objects for reliable analysis.

## Testing Strategy

Before building broad UX polish, validate the control loop.

Test categories:

- happy path consensus
- productive disagreement
- no-consensus termination
- one CLI timeout
- malformed agent output
- repeated argument detection
- user final question after consensus
- user final question after no consensus

Use a canned transcript harness if possible so orchestration logic can be tested without live model calls every time.

## Open Product Decisions

These decisions should be made before implementation starts.

1. Is the debate transcript fully visible to the user, or summarized by default?
2. Does the user pick the starting agent every time, or can the system auto-route?
3. Should the final question go to:
   - one chosen agent,
   - both agents,
   - or the orchestrator only?
4. Is “consensus required” or is “ranked recommendation” sufficient?
5. Is the first release strictly read-only, or can the agents inspect repository context?

## Recommended Implementation Order

1. Define the normalized turn schema and consensus schema.
2. Build adapters for Claude CLI and Codex CLI with stable process handling.
3. Build the orchestrator state machine with a fixed debate protocol.
4. Add transcript persistence and replay.
5. Build a minimal chat UI around the orchestrator.
6. Add final-question handling.
7. Add observability and failure recovery.
8. Iterate on prompt design and stop conditions using real transcripts.

## Practical Recommendation

If the goal is to get something working quickly, do not start with “two free-form agents chatting.” Start with “two agents filling constrained debate turns under an orchestrator-owned state machine.”

That is the difference between:

- a demo that looks clever but is unstable
- and a tool that can consistently produce useful planning output

## Deliverable Definition

The first successful version should let the user do exactly this:

1. Choose `Claude` or `Codex`.
2. Enter one prompt.
3. Watch a bounded debate.
4. Receive either:
   - an agreed plan, or
   - a structured decision brief.
5. Ask one final question like “summarize this for implementation.”
6. Copy the result into another agent or workflow.

That is a sensible and buildable version of the idea.
