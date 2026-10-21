# Development and verification

The current runtime is C# with a React/Vite dashboard. It provides one open workspace, service-owned execution, CLI and REST participants, and local tools for REST models. Accounts, pairing, roles, security audit, backup/restore workflows and external telemetry services are outside product scope. Historical database tables and upgrade safeguards remain for compatibility.

`go-tui` and `go-web` build and launch the supported interfaces. Node.js and npm are used only for dashboard development and builds. See IMPROVEMENTS.md for remaining acceptance work; passing fixture tests does not close all release gates.

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

The verification workflow builds and tests on Windows, macOS and Linux, checks dashboard/browser behavior and generated artifact drift, and exercises PostgreSQL and the local runner fixture. Runtime data, private credentials and test evidence must stay outside tracked fixtures. Remaining packaging, parity, live-provider and release gates are documented in IMPROVEMENTS.md.
