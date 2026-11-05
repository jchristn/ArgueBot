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

Container acceptance uses `docker compose -f docker/compose.yaml -f docker/compose.build.yaml build` followed by `node scripts/test-container.mjs`. It creates a unique Compose project, loopback port and disposable data/key/work volumes, runs the committed Postman provider fixture inside the application container, checks packaged executables and four-seat consensus, then replaces the container and verifies encrypted credentials and key recovery. Only that test project's volumes are removed. Set `DOCKER_HOST` to use a separate daemon without changing the active Docker context. Logs are retained in `.run/container-*/container.log`. These checks do not authenticate with live model providers.

Focused console suites include `--suite Storage`, `--suite Service`, `--suite Terminal`, `--suite Configuration`, `--suite Security`, `--suite Runners`, `--suite Providers` and `--suite Packaging`. The Security suite concerns local certificate trust, not product users. Windows launcher parity must also be verified on Windows.

Distribution builds use `publisher.json` and `ArgueBot.Publisher`, with native packaging jobs in `.github/workflows/packaging.yaml`. Build the dashboard, then run `build-installers.sh --runtime osx-arm64` on macOS or the corresponding native target on Linux/Windows. The publisher's `--doctor` checks prerequisites and the exact reviewed SDK. `scripts/test-packaged.mjs` runs an extracted bundle with a disposable workspace. `scripts/test-linux-installer.mjs` additionally installs, replaces and removes a `.deb` or `.rpm` on an isolated Linux host; it refuses to overwrite an existing application. It needs package-manager privileges and preserves the test database until verification completes. See [packaging instructions](../packaging/README.md) for dependency locks, toolchain pins, unsigned notices and the distinction between built, tested and published artifacts.

## Start an isolated service

```sh
dotnet run --project src/ArgueBot.Server -c Release --no-build -- --home .run/home --port 4417
dotnet run --project src/ArgueBot.Cli -c Release --no-build -- --home .run/home --server http://127.0.0.1:4417 conversations create "Roadmap" --question "What should we build?"
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

Failure-guidance regression coverage classifies stdout/stderr/provider capture limits, rejects raw diagnostic disclosure and handles missing, malformed or mismatched historical captures. The terminal service journey forces a real child-process output overflow, verifies errorCode/errorMessage through HTTP and checks visible transcript guidance in light/dark themes. It also stops a run, renames its conversation and presses b to verify the original prompt remains editable. Storage checks verify the reviewed replacement question survives run persistence. Dashboard tests verify the failed-turn alert includes the cause and recovery without opening an inspector.

`Runners.GenerationTimeout` exercises a real fixture process through an isolated engine, verifying that deadline failure persists `generation_timeout`, retains its partial answer, and explains frozen-timeout recovery. A separate explicit interruption must remain interrupted. `Runners.RestDeadline` calls the committed Postman `deadline-stream` scenario over real HTTP, receives partial text, and verifies deadline versus caller-cancellation behavior. The dashboard `generation-deadline.spec.js` test observes the timer, waits for the real subprocess deadline, reloads the saved failure and inspects the historical timeout. These checks use no real credentials or production homes. See [generation diagnostics](GENERATION_DIAGNOSTICS.md) for live-investigation boundaries.

`Runners.ClaudeIndexedResults` replays the second monitored failure: a successful `result_index: 0` repeats an earlier assistant message after newer text has streamed. It verifies durable completion, exact nonduplicated answer text, previously unseen indexed result text and rejection of conflicting replacements for one result index. The original legacy conflicting-snapshot and malformed/empty/error result checks remain active. Fixtures contain fictional content and no live deployment details.
