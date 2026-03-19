<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/arguebot/main/icon.png" alt="ArgueBot" width="192" height="192" />
</p>

<h1 align="center">ArgueBot</h1>

<p align="center">
  <strong>Let Claude Code, Codex, or Gemini debate a topic so you don't have to guess.</strong>
</p>

<p align="center">
  <a href="#why-arguebot">Why</a> &middot;
  <a href="#how-it-works">How It Works</a> &middot;
  <a href="#getting-started">Getting Started</a> &middot;
  <a href="#commands">Commands</a> &middot;
  <a href="#license">License</a>
</p>

---

## Why ArgueBot?

You ask an AI a question and get a confident answer. But is it the *right* answer? A single model gives you a single perspective. It won't challenge its own assumptions, probe its own blind spots, or stress-test its own reasoning.

ArgueBot fixes this by pitting two frontier coding agents against each other in a structured debate. You pose a question, pick who opens, and watch them argue it out -- surfacing trade-offs, catching weak assumptions, and forcing each other to defend their positions with concrete reasoning.

When they reach genuine consensus, you know the answer has survived adversarial scrutiny. When they don't, you get a clear map of the disagreements and trade-offs -- which is often more valuable than a single confident answer.

**Use it when the stakes are high enough to want a second opinion, but you don't have a second expert in the room.**

- Architecture decisions before you commit to a direction
- Debugging strategies when the root cause isn't obvious
- Code review disputes where both sides have merit
- Technology evaluations with real trade-off analysis
- Any question where "it depends" deserves a real exploration of *what* it depends on

## How It Works

```
 You                    Orchestrator               Agent A          Agent B
  |                         |                         |               |
  |--- prompt ------------->|                         |               |
  |                         |--- opening prompt ----->|               |
  |<-- streaming response --|<-- streamed response ---|               |
  |                         |                         |               |
  |  (intervention window)  |                         |               |
  |                         |--- rebuttal prompt -----|-------------->|
  |<-- streaming response --|<------------------------|-- streamed ---|
  |                         |                         |               |
  |  (intervention window)  |                         |               |
  |                         |    ...continues...      |               |
  |                         |                         |               |
  |                         |<--- "WE HAVE CONSENSUS" |               |
  |<-- consensus reached! --|                         |               |
  |                         |                         |               |
  |--- follow-up question ->|--- summary prompt ----->| (or Agent B)  |
  |<-- streaming answer ----|<-- streamed answer -----|               |
```

1. **You write one prompt** -- a question, a problem, an architectural decision.
2. **Two agents debate** -- alternating turns with full transcript context. Each agent sees every prior argument and must respond substantively.
3. **You can intervene** -- between any two turns, a countdown window lets you inject moderator notes, steer the conversation, pause, or stop early.
4. **Consensus is explicit** -- agents are instructed to write "WE HAVE CONSENSUS" only when they genuinely agree. Partial agreement doesn't count.
5. **You ask the final question** -- choose which agent summarizes the result, then take that output wherever you need it.

Responses stream in real-time so you're never staring at a blank screen.

Prompt input is multiline. When a prompt opens, use `ENTER` to add a newline and `ESC` then `ENTER` to submit the buffer exactly as typed.

## Getting Started

### Prerequisites

- [Node.js](https://nodejs.org/) 18+
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed and authenticated
- [Codex CLI](https://github.com/openai/codex) installed and authenticated
- [Gemini CLI](https://google-gemini.github.io/gemini-cli/) installed and authenticated if you want Gemini as one of the debaters

### Install

```bash
git clone https://github.com/jchristn/arguebot.git
cd arguebot
npm install
```

### Configure API Keys

ArgueBot now loads a local `.env` file automatically at startup and passes those
variables through to the agent CLIs it spawns.

Create `.env` in the project root with the providers you plan to use:

```bash
OPENAI_API_KEY=your_openai_key
ANTHROPIC_API_KEY=your_anthropic_key
GEMINI_API_KEY=your_gemini_key
```

If you already exported these in your shell, that also works. A local `.env`
is the simplest way to make provider auth deterministic inside ArgueBot.

### Provider Auth Notes

ArgueBot runs all three CLIs in non-interactive headless mode:

- Claude uses `claude -p - --output-format text`
- Codex uses `codex exec ... -`
- Gemini uses `gemini --prompt "" --output-format text`

That matters because each CLI may use a different auth path in headless mode
than in the interactive terminal UI.

#### Claude

- Claude can work either from `ANTHROPIC_API_KEY` or from its stored Claude
  Code login.
- ArgueBot checks for `ANTHROPIC_API_KEY` first, then falls back to Claude's
  local credentials under `~/.claude/.credentials.json`.
- If Claude works directly in your terminal but not under ArgueBot, make sure
  the same user profile is launching both processes and that `claude auth`
  completed for that profile.

#### Codex

- Codex can work either from `OPENAI_API_KEY` or from its stored `codex login`
  credentials.
- ArgueBot checks for `OPENAI_API_KEY` first, then falls back to Codex's local
  credentials under `~/.codex/auth.json`.
- If you want the most predictable setup, put `OPENAI_API_KEY` in `.env` even
  if `codex login` already works interactively.

#### Gemini

- Gemini CLI has multiple auth modes, and the selected mode affects headless
  behavior.
- If `~/.gemini/settings.json` has `security.auth.selectedType` set to
  `gemini-api-key`, Gemini will require `GEMINI_API_KEY` in the environment at
  runtime. A prior `/auth` inside the interactive Gemini UI does not replace
  that env var requirement.
- If you want Gemini to work without exporting `GEMINI_API_KEY`, run
  `gemini`, then `/auth`, and switch the selected auth mode to Google login.
- If you use Vertex AI mode instead, set either `GOOGLE_API_KEY` or both
  `GOOGLE_CLOUD_PROJECT` and `GOOGLE_CLOUD_LOCATION`.

### Run

```bash
npm start
```

Or with options:

```bash
npm start -- --rounds 8 --timeout 15

npm start -- --first gemini --second codex --summary gemini --yolo
```

> **Note:** The `--` after `npm start` is required so npm passes the flags to ArgueBot instead of consuming them itself.

### Example Session

```
ArgueBot v0.1 -- Multi-Agent Debate
───────────────────────────────────────────────

Choose who goes first [default claude]: [c]laude / code[x] / [g]emini > c
Choose the opposing agent [default codex]: [c]laude / code[x] / [g]emini > x
Max rounds (default 5) > 3
Which agent for summary [default claude]: [c]laude / code[x] > x

Enter the prompt you wish to send to the first agent to begin. ENTER = newline, ESC then ENTER = submit.
> Debate why the sky is blue
  and come to a consensus.

[Round 1/3]

Claude Code >
The sky is blue primarily due to Rayleigh scattering. When sunlight
enters the atmosphere, shorter wavelengths (blue, violet) scatter more
strongly off nitrogen and oxygen molecules, following a 1/lambda^4
relationship. Although violet scatters even more than blue, our eyes
are more sensitive to blue and the Sun emits more blue than violet...

Continue in 8s... (type to intervene)
ENTER = newline, ESC then ENTER = submit.
You > /steer "Challenge whether vision matters to the explanation."

Codex >
I'd push back on calling Rayleigh scattering a complete explanation.
The scattering cross-section is necessary but not sufficient -- you
also need the solar emission spectrum and human photopic response to
get from "short wavelengths scatter more" to "the sky looks blue."
Without those, a 1/lambda^4 law alone could yield violet, not blue...

Continue in 8s... (type to intervene)
ENTER = newline, ESC then ENTER = submit.
You >

[Round 2/3]

Claude Code >
Fair point. I'll concede that the solar spectrum and human vision are
load-bearing parts of the explanation, not optional footnotes...

Codex >
WE HAVE CONSENSUS

The sky appears blue because: (1) Rayleigh scattering preferentially
scatters shorter wavelengths via a 1/lambda^4 dependence, (2) the
solar spectrum peaks in visible wavelengths with sufficient blue
emission, and (3) human photopic vision is more sensitive to blue
than violet. All three factors are necessary for the complete answer.

>>> Consensus reached! <<<

ENTER = newline, ESC then ENTER = submit.
Follow-up > Summarize in one sentence.

Codex >
The sky is blue because molecular scattering favors short wavelengths,
the Sun emits enough blue light, and human eyes are more sensitive to
blue than to the even-more-scattered violet.
```

## CLI Options

| Flag | Description | Default |
|------|-------------|---------|
| `--rounds N` | Max debate rounds | `5` (prompted) |
| `--timeout N` | Intervention window (seconds) | `10` |
| `--agent-timeout N` | Agent response timeout (seconds) | `300` |
| `--yolo` | Enable dangerous/no-confirmation mode for supported agent CLIs | `false` |
| `--first claude\|codex\|gemini` | Which agent opens | prompted |
| `--second claude\|codex\|gemini` | Which agent is the other debater | prompted |
| `--summary claude\|codex\|gemini` | Which agent handles summary | prompted |
| `--help` | Show help | |

**Example:**

```bash
npm start -- --rounds 8 --agent-timeout 600 --first gemini --second claude --summary gemini --yolo
```

## Commands

During the debate, a countdown window appears between turns. If you start typing, the timer stops and the multiline editor opens. Use `ENTER` for newline and `ESC` then `ENTER` to submit.

| Command | Effect |
|---------|--------|
| *(wait without typing)* | Continue to next turn |
| *(any text)* | Inject a moderator note into the transcript |
| `/steer "focus on X"` | Add a directive to the next agent's prompt |
| `/pause` | Pause the debate (resume with `/resume`) |
| `/stop` | End debate early, go to follow-up Q&A |
| `/extend N` | Add N more rounds |
| `/reset` | Scrap the debate and start over |
| `/help` | Show command list |

During follow-up Q&A, prompts use the same multiline controls: `ENTER` adds a newline and `ESC` then `ENTER` submits.

| Command | Effect |
|---------|--------|
| *(any text)* | Ask a follow-up question (answered by your chosen summary agent) |
| `/save` | Export the full transcript to a markdown file |
| `/transcript` | Show a summary of all turns |
| `/done` | Exit |

## Project Structure

```
src/
  index.ts          Entry point and CLI arg parsing
  orchestrator.ts   Debate state machine and main loop
  agents.ts         Agent driver -- spawns CLI subprocesses with streaming
  prompts.ts        Prompt templates for opening, rebuttal, and summary
  consensus.ts      Consensus detection (looks for "WE HAVE CONSENSUS")
  ui.ts             Terminal UI -- colors, countdown timer, input handling
  transcript.ts     Markdown transcript export
  types.ts          Shared TypeScript types and defaults
```

## Bugs and Issues

Found a bug or have a feature request? Please open an issue:

https://github.com/jchristn/arguebot/issues

When filing a bug, include:
- The prompt you used
- Which agents were involved
- The error message or unexpected behavior
- Your Node.js version (`node --version`)

## License

[MIT](LICENSE.md) -- see [LICENSE.md](LICENSE.md) for details.
