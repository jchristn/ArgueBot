<p align="center">
  <img src="https://raw.githubusercontent.com/jchristn/arguebot/main/icon.png" alt="ArgueBot" width="192" height="192" />
</p>

<h1 align="center">ArgueBot</h1>

<p align="center"><strong>Put agents in conversation, challenge their answers, and keep the history.</strong></p>

ArgueBot runs structured debates between two or three participants and supports follow-up questions to individual agents. Its C# TUI and React web dashboard share one persistent service, so you can switch interfaces without losing conversations or stopping accepted work. A scriptable C# CLI uses that same service.

Participants can use Claude Code, Codex, Gemini CLI, Mux, a generic CLI harness, or REST models through PolyPrompt. REST participants can execute local file and process tools on the service machine. Agent-declared consensus is recorded as an outcome, not treated as proof that the answer is correct.

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

## Configure participants

In the TUI, use **Agents** or **Ctrl+E**. In the dashboard, open **Participants** and select **Add participant**.

Choose a named runner type to populate its executable and arguments. Claude Code includes `--dangerously-skip-permissions`; Codex includes `--yolo` so accepted debate turns can run unattended. You can edit the argument rows. Generic CLI requires an executable and any arguments its protocol needs. Each argument row is one literal flag or value, without shell quoting.

Installed harnesses must be available to the service process and configured with their own provider credentials or sign-in. A working directory selects where the harness or local tools operate. Do not enter a whole shell command in the executable field.

For a REST participant, choose a provider, endpoint and model. Supported provider formats include Ollama, OpenAI-compatible, Anthropic and Gemini. Configure any credential through the named environment variable available to the service process. ArgueBot does not automatically load a project `.env` file. Restart the service when its environment changes.

CLI harnesses and REST local tools execute on the service machine, within the configured local-execution policy. Historical run snapshots retain the participant settings used for that run even if you edit the reusable profile later. See [execution behavior](docs/EXECUTION.md).

## Terminal interface

Use **Start debate** to enter a question, choose speakers, set debate options and review before submission. The service runs the debate while the TUI displays committed history and current state.

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

- Click an editable table row to open its editor. Explicit copy and action buttons remain independent.
- Conversation and participant IDs have dedicated columns, monospace text and copy buttons.
- **Open conversation** opens its workspace. Copy individual turns or the full conversation; complete content is fetched independently of visible previews.
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
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll --home .run/my-workspace conversations create "Which design should we choose?"
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
