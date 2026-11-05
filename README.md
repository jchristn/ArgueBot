<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/arguebot/main/icon.png" alt="ArgueBot" width="192" height="192" />
</p>

<h1 align="center">ArgueBot</h1>

<p align="center"><strong>Put agents in conversation, challenge their answers, and keep the history.</strong></p>

ArgueBot runs structured debates between a configurable panel of agents and supports follow-up questions to individual agents. Its C# TUI and React web dashboard share one persistent service, so you can switch interfaces without losing conversations or stopping accepted work. A scriptable C# CLI uses that same service.

Participants can use Claude Code, Codex, Gemini CLI, Mux, a generic CLI harness, or REST models through PolyPrompt. REST participants can execute local file and process tools on the service machine. Agent-declared consensus is recorded as an outcome, not treated as proof that the answer is correct.

## About v2.0.0

**v2.0.0 is a complete rewrite of ArgueBot.** The original Node.js prototype (version 0.1.0, run through npm) let two or three CLI agents debate and detected agreement from keywords. Version 2.0.0 replaces it with a C# service, a terminal interface (TUI), a React web dashboard and a scriptable CLI that all share one persistent workspace. The npm commands are retired.

What v2.0.0 does:

- **Multi-agent debates.** A panel of 2 to 16 agents (configurable up to 64): Claude Code, Codex, Gemini CLI, Mux, a generic CLI, or REST models served through Ollama or an OpenAI-compatible endpoint. An orchestrator asks one focused question per round, the agents respond, the orchestrator proposes an answer and every agent votes. Agreement requires an explicit "agree" from every active agent on the same proposal; disagreement stays visible.
- **You moderate.** Add context for everyone or privately for one agent, either immediately or at the next turn; pin constraints; edit the proposal under vote; add, remove or replace agents between turns; pause, resume, interrupt, extend or stop.
- **Long debates stay grounded.** The orchestrator keeps a source-linked working memory, and agents use built-in tools to page through turns, search the conversation, check evidence and ask you for clarification. Original turns are never discarded.
- **Editable prompts.** Each debate step's instructions come from a prompt library. Factory defaults are read-only; duplicate one to change it, using `{parameters}` such as `{question}`, and choose prompts per conversation.
- **Reusable configuration.** Models, CLI harnesses, HTTP endpoints and encrypted credentials (Claude Code tokens, Codex logins, bearer, header and query API keys) are defined once and reused. Execution budgets, timeouts and output limits are set per run.
- **Readable results.** Watch a debate live in a compact timeline, then read it as a full-page transcript with rounds, the final proposal and each agent's vote. Export as Markdown or print to PDF.
- **One service, many interfaces.** The service owns the database (SQLite by default, PostgreSQL optional) and keeps running when you close an interface. The TUI, dashboard, CLI, REST API and Docker image all use it.

**Status:** v2.0.0 is an implementation preview on `main` and has not been published as a release. Distribution builds are local artifacts: Windows packages are unsigned and macOS bundles are ad-hoc signed and not notarized. Databases from earlier v2 previews upgrade automatically on startup, with a backup first. See the [CHANGELOG](CHANGELOG.md) for details, the [debate guide](docs/MULTIPLE_AGENTS.md), [prompts](docs/PROMPTS.md), the [model tools](MCP_API.md), the [Docker guide](docker/README.md) and [packaging](packaging/README.md).

If the operating system blocks a downloaded build, review the source and checksum first. Then on Windows choose **More info > Run anyway**, and on macOS choose **Privacy & Security > Open Anyway**.

## Getting started

Source builds require the .NET 10 SDK. The repository pins its SDK in [global.json](global.json). Building the web dashboard also requires Node.js 24 and npm; the terminal interface does not require npm. The shared library/test matrix additionally uses .NET 8.

Clone the repository and choose an interface:

| Interface | macOS / Linux | Windows |
| --- | --- | --- |
| Terminal | `./go-tui.sh` | `go-tui.bat` |
| Web dashboard | `./go-web.sh` | `go-web.bat` |
| Stop service | `./stop-web.sh` | `stop-web.bat` |

Both launchers build before running. The web launcher installs dashboard dependencies when needed, starts or attaches to the selected service, and prints the URL to open in your browser. The default URL is <http://127.0.0.1:4317/dashboard>. Re-running it reuses the service for that application home.

The workspace opens directly without accounts, login, pairing or special links. Docker and external telemetry services are not required.

### Choose an application home

Conversations, configuration and logs live in `~/.arguebot` by default. Use the same home in both interfaces to work with the same data:

```sh
./go-tui.sh --home .run/my-workspace
./go-web.sh --home .run/my-workspace --port 4417
./stop-web.sh --home .run/my-workspace
```

A port override applies when starting a service. Attaching to an existing service uses its actual listener address. `--server URL` connects to an explicit service. The service owns its home and database exclusively; clients never write the database directly.

## Docker

The published image `jchristn77/arguebot` runs the service and dashboard with pinned Claude Code, Codex and Gemini CLIs:

```sh
docker compose -f docker/compose.yaml up -d
```

Add `-f docker/compose.build.yaml` to build from this checkout instead. Maintainers publish images with `build-arguebot.sh <tag>` or `build-arguebot.bat <tag>` (`build-all` runs every image build). See the [Docker guide](docker/README.md) for volumes, environment settings and the trust model.

## Configure participants

In the TUI, use **Agents** or **Ctrl+E**. In the dashboard, open **Participants** and select **Add agent** on the Agents tab.

Choose a named runner type to populate its executable and arguments. Claude Code includes `--dangerously-skip-permissions`; Codex includes `--yolo` so accepted debate turns can run unattended. You can edit the argument rows. Generic CLI requires an executable and any arguments its protocol needs. Each argument row is one literal flag or value, without shell quoting.

Installed harnesses must be available to the service process and configured with their own provider credentials or sign-in. A working directory selects where the harness or local tools operate. Do not enter a whole shell command in the executable field.

For a REST participant, choose a provider, endpoint and model. Supported provider formats include Ollama, OpenAI-compatible, Anthropic and Gemini. Configure any credential through the named environment variable available to the service process. ArgueBot does not automatically load a project `.env` file. Restart the service when its environment changes.

CLI harnesses and REST local tools execute on the service machine, within the configured local-execution policy. Historical run snapshots retain the participant settings used for that run even if you edit the reusable profile later. See [execution behavior](docs/EXECUTION.md).

## Terminal interface

Use **Start debate** to enter a question, choose speakers and an orchestrator, set debate options and review before submission. The service runs the debate while the TUI displays committed history and current state.

Useful controls include:

- **F1**: explanatory help for the current context.
- **F2**: action menu.
- **Tab / Shift+Tab**: move focus; **Alt+1 / Alt+2 / Alt+3**: jump between conversations, history and notes.
- In the conversation list, **Enter** opens the highlighted conversation, **s** offers to stop unfinished work, and **d / Delete** offers deletion. Deleting unfinished work offers a combined stop-and-delete flow.
- **Ctrl+B**: configure a debate; **Ctrl+R**: moderator controls; **Ctrl+Y**: follow-up question.
- **Ctrl+L**: full message history; **Ctrl+G**: generation attempts; **Ctrl+T**: theme.
- **Ctrl+Q**: detach. Accepted work continues in the service.

After consensus, open moderator controls for final instructions or use a follow-up for a specific participant. Detailed workflows, confirmation behavior and failure recovery are explained in [terminal usage](docs/TUI.md) and the built-in help.

## Web dashboard

The dashboard provides conversations, participant configuration, the conversation workspace, request history, API Explorer and service settings.

- **New conversation** asks for a title, shown in the conversation list, and an optional opening question. Starting a debate pre-fills its question from the opening question (or the title when it is blank); you can edit it before the debate starts.
- Click a conversation row to open its workspace; click another editable row to open its editor. Each table's **Actions** column has a menu (for conversations: open, rename, pin, archive, stop debate, view JSON and delete). Copy buttons remain independent.
- **Prompts** lists the instructions each debate step sends. Factory defaults are read-only; duplicate one to edit it, with a legend of the `{parameters}` it can use, then choose it per step when creating a conversation or starting a debate. See [docs/PROMPTS.md](docs/PROMPTS.md).
- Participants has tabs for agents, models, CLI harnesses, HTTP endpoints and credentials. The Credentials tab explains how to obtain each kind of credential, with copyable commands for macOS, Linux and Windows PowerShell, and can reveal a stored value after confirmation.
- Conversation and participant IDs have dedicated columns, monospace text and copy buttons.
- **Read transcript** (in the workspace header and each conversation's Actions menu) opens the whole conversation as a full-page document: contents by debate and round, the final proposal with every agent's vote, each round's question, responses and vote table, and the moderator's context. Agents keep their colors and can be hidden one at a time, tool calls are folded away unless you show them, every turn folds, and the page copies as Markdown or prints and saves as PDF.
- In the workspace, the default **Timeline** view shows one compact line per turn (agent color, step, vote position, a preview of the text, and the newest text while an agent is still writing), numbers each round and folds consecutive tool calls into one row. Click a line to expand it in place; failed turns open automatically. **Cards** shows every turn in full. Both views support Expand all, Collapse all and **Jump to turn**. Copy individual turns or the full conversation; complete content is fetched independently of visible previews.
- Clipboard support includes HTTPS, localhost and other HTTP origins, with a manual selection fallback when the browser blocks clipboard access.
- Diagnostic request rows open an inspector with metadata, request/response captures and copy controls.
- Request activity offers last-hour/day/week/30-day ranges with 60 one-minute, 96 fifteen-minute, 84 two-hour and 90 eight-hour buckets. Hover or focus the chart for interval details. Green represents HTTP 100–399; red represents HTTP 400–599.
- API Explorer selects operations from a dropdown above the request editor.
- Click outside a modal or press Escape to dismiss it.

Closing either interface does not stop a debate. Use the conversation's stop controls to stop a run, or `stop-web` to shut down the service. Request history and local operational logs are built in; no separate monitoring stack is needed.

## Scriptable CLI

Build the solution, then use the CLI against the same application home:

```sh
dotnet build src/ArgueBot.sln -c Release
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll --help
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll --home .run/my-workspace conversations list
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll --home .run/my-workspace conversations create "Design choice" --question "Which design should we choose?"
```

Commands support participant management, notes, debate controls, generation-attempt inspection and transcript export. JSON inputs can come from stdin or `--file PATH`. Conversation export writes complete Markdown; `conversations show ID --jsonl` streams full message records. See [REST_API.md](REST_API.md) for the service contract and generated API artifacts.

## Development

The service supports SQLite by default and PostgreSQL when configured. Tests use isolated application homes, disposable provider databases and local model/process fixtures. Historical database tables and automatic upgrade safeguards remain for existing installations; backup/restore and identity management are not product workflows.

```sh
npm --prefix dashboard ci
npm --prefix dashboard run build
dotnet restore src/ArgueBot.sln --locked-mode
dotnet build src/ArgueBot.sln -c Release --no-restore
dotnet run --project src/Test.Automated -c Release --no-build
npm --prefix dashboard run test:e2e
```

The source layout is:

| Path | Purpose |
| --- | --- |
| `src/ArgueBot.Core` | Persistent conversations, execution engine and database methods |
| `src/ArgueBot.Runners` | CLI and PolyPrompt runners plus local tool execution |
| `src/ArgueBot.Server` | Service API, dashboard hosting and execution ownership |
| `src/ArgueBot.Client` | Shared native API client and local service attachment |
| `src/ArgueBot.Tui` | TUIKit terminal interface |
| `src/ArgueBot.Cli` | Scriptable CLI |
| `dashboard` | React/Vite web dashboard and browser tests |
| `src/Test.*` | Shared checks, test adapters and fictional process fixtures |
| `assets` | API contracts, localization, harness defaults and dependency inventory |
| `postman` | Local provider simulations |

See [development and verification](docs/DEVELOPMENT.md), [service operations](docs/OPERATIONS.md), [terminal usage](docs/TUI.md) and [execution behavior](docs/EXECUTION.md). [ROADMAP.md](ROADMAP.md) tracks remaining platform, packaging, live-provider and release acceptance work; interface consolidation does not claim all acceptance gates have passed.

## License

MIT. See [LICENSE.md](LICENSE.md).

## AOT support

Not supported. The service and persistence layer use reflection-based `System.Text.Json` serialization of runtime-selected request and provider types, without generated serialization metadata. The current application and dependency stack has not been made trim-safe. Release builds and the full .NET 8/.NET 10 shared test matrix are JIT builds; they do not establish native AOT compatibility.
