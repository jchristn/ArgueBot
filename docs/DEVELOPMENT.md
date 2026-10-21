# Development and verification

The current runtime is C# with a React/Vite dashboard. It provides one open workspace, service-owned execution, CLI and REST participants, and local tools for REST models. Accounts, pairing, roles, security audit, backup/restore workflows and external telemetry services are outside product scope. Historical database tables and upgrade safeguards remain for compatibility.

`go-tui` and `go-web` build and launch the supported interfaces. Node.js and npm are used only for dashboard development and builds. See [ROADMAP.md](../ROADMAP.md) for remaining acceptance work; passing fixture tests does not close all release gates.

## Build and test

Use .NET 10 for applications, plus .NET 8 for shared library and test targets. NuGet.Config selects an ignored repository-local cache and verifies locked dependencies against published packages.

```sh
npm --prefix dashboard ci
npm --prefix dashboard run lint
npm --prefix dashboard run i18n:check
npm --prefix dashboard run test:ci
npm --prefix dashboard run build
dotnet restore src/ArgueBot.sln --locked-mode
dotnet build src/ArgueBot.sln -c Release --no-restore
dotnet run --project src/Test.Automated -c Release --no-build -- --results .run/console-results.json
dotnet test src/Test.Xunit -c Release --no-build
dotnet test src/Test.Nunit -c Release --no-build
node scripts/test-cli-surfaces.mjs
node scripts/test-stop-web.mjs
npm --prefix dashboard run test:e2e
```

All fixtures use isolated application homes and loopback listeners. Console descriptors are shared with xUnit and NUnit. Optional provider and runner integrations report explicit skips when their fixture variables are absent. For real CLI parsing and process cancellation checks, set `ARGUEBOT_CLI_FIXTURE` to the built `src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll`. These fictional harnesses require no paid provider credentials.

Focused console suites include `--suite Storage`, `--suite Service`, `--suite Terminal`, `--suite Configuration`, `--suite Security`, `--suite Runners` and `--suite Providers`. The Security suite concerns local certificate trust, not product users. Windows launcher parity must also be verified on Windows.

## Start an isolated service

```sh
dotnet run --project src/ArgueBot.Server -c Release --no-build -- --home .run/home --port 4417
dotnet run --project src/ArgueBot.Cli -c Release --no-build -- --home .run/home --server http://127.0.0.1:4417 conversations create "What should we build?"
dotnet run --project src/ArgueBot.Tui -c Release --no-build -- --home .run/home --server http://127.0.0.1:4417
```

Clients start or attach to their selected home automatically unless `--no-start` or an explicit `--server` is supplied. No account or token is required. Agent providers may require their own credentials. See [terminal usage](TUI.md), [execution](EXECUTION.md) and [operations](OPERATIONS.md).

## Provider fixtures

Disposable PostgreSQL fixtures are defined separately in `docker/providers/compose.yaml`. Never point integration tests at an existing database. Set `ARGUEBOT_TEST_POSTGRESQL` to an explicitly provisioned loopback fixture connection. Shared lifecycle tests preserve one exclusive database writer.

The committed Postman simulation runs provider-native success and failure cases, including REST models calling real local file/process tools:

```sh
postman simulation run postman/simulations/runners.sim.yaml --port 4491 --output ndjson
# In another terminal:
ARGUEBOT_SIMULATION_URL=http://127.0.0.1:4491 dotnet run --project src/Test.Automated -c Release --no-build
```

The CI fixture can also run directly using `PORT=4491 node --input-type=commonjs < postman/mocks/ollama/default.js`. Use .run for ignored local logs, captures and evidence. Simulated transport and fictional process checks do not establish paid-provider compatibility.

## Generated artifacts and CI

`node scripts/generate-api.mjs` produces the OpenAPI document, Postman collection, REST_API.md and transport metadata from the active contract and typed DTOs. `node scripts/generate-dependencies.mjs` records exact dependency versions, integrity hashes and declared licenses after a locked solution restore. Commit generated artifacts alongside their source changes.

The verification workflow builds and tests on Windows, macOS and Linux, checks dashboard/browser behavior and generated artifact drift, and exercises PostgreSQL and the local runner fixture. Runtime data, private credentials and test evidence must stay outside tracked fixtures. Remaining packaging, parity, live-provider and release gates are documented in [ROADMAP.md](../ROADMAP.md).

## CLI failure diagnostics regression coverage

`Runners.CliDiagnostics` runs fictional child processes for malformed JSON, a JSON field-type mismatch, conflicting Codex snapshots, nonzero exit, timeout and stdout/stderr limits. It checks bounded stdout/stderr and triggering-record retention, Unicode, parser paths, exit status and cancellation semantics. An isolated SQLite engine test verifies that the capture survives engine disposal, identifies the failed attempt and response, prunes the oldest of 100 captures, protects file permissions on Unix and preserves attempt completion when the diagnostic destination is unavailable. CI already supplies `ARGUEBOT_CLI_FIXTURE`; without it these process tests explicitly skip. These checks cover evidence retention, not compatibility with every version of a real agent CLI. New real-provider failures still need sanitized replay fixtures once the retained record is available.

The terminal debate-setup regression checks button styles and rendered focused/disabled cells in both light and dark themes before and after forward/back navigation and on the final review step. The wizard reapplies the active button theme before rendering because toolkit step changes rebuild its buttons with default styles. This prevents bright default teal buttons from appearing alongside the shared muted focus palette.

`Runners.ClaudeToolReplay` replays sanitized Claude Code stream-json event shapes from the October 21, 2026 failure into a real child process. The original failure was a string-valued `tool_result.content` rejected by a recursive message DTO at `$.message.content[0].content`. The checked-in fixtures contain fictional content, identifiers and relative paths. They include tools before text, string/array/object tool payloads, multiple assistant message IDs, text-block starts and deltas, reasoning blocks, repeated snapshots, nested-agent events and final results. Assertions require exact nonduplicated answer text and a durable completed run, attempt and response. Negative process cases reject empty/error results, conflicting snapshots and EOF without a final result. The replay fails against the old parser at the same JSON path as the production capture. This is deterministic protocol regression evidence; no paid live-provider call is made by this test.
