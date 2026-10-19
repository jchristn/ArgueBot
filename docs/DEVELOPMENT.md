# Replacement runtime development

The C# replacement is under active implementation. The npm entry points still run the existing application. Do not use the replacement as a finished hosted product: full RBAC, provider backup/restore, full dashboard/TUI parity, packaging and acceptance gates remain open in IMPROVEMENTS.md.

NuGet.Config uses a repository-local ignored package cache and maps packages to nuget.org. Locked hashes are verified against published packages, avoiding same-version private builds in a machine-wide cache.

Build from the repository root with .NET 10 (and .NET 8 for reusable-library tests):

```sh
dotnet restore src/ArgueBot.sln --locked-mode
dotnet build src/ArgueBot.sln -c Release --no-restore
dotnet run --project src/Test.Automated -c Release -f net10.0
dotnet test src/Test.Xunit -c Release -f net10.0
dotnet test src/Test.Nunit -c Release -f net10.0
```

Use a disposable home for development. The service requires local pairing and stores session digests, conversations, messages, events, run checkpoints and immutable participant snapshots in its database. It binds only to 127.0.0.1. No application version was changed.

```sh
dotnet run --project src/ArgueBot.Server -c Release -f net10.0 -- --home .run/home --port 4417
dotnet run --project src/ArgueBot.Cli -c Release -f net10.0 -- --home .run/home --server http://127.0.0.1:4417 conversations create "What should we build?"
dotnet run --project src/ArgueBot.Tui -c Release -f net10.0 -- --home .run/home --server http://127.0.0.1:4417
```

Local clients exchange the protected `.run/home/runtime/pairing-code` for a revocable session. Every exchange rotates the code. When --server is omitted, local clients start or attach to the service for their selected home. --no-start requires it to be running. Explicit --server addresses attach without starting processes. Use `service start|status|restart|stop` in the console to manage it. Stopping and restarting require an administrator session. For a remote client, set `ARGUEBOT_TOKEN` from an authorized session; the current replacement server is deliberately loopback-only. Never paste real tokens into committed commands or URLs.

CLI `participants add --file profile.json` accepts a profile with name, kind (`cli` or `rest`), protocol provider, executable/ordered arguments or REST endpoint/model, optional workingDirectory, and secretEnvironmentVariable. Prompt content goes over stdin for CLI runners. `.cmd` and `.bat` wrappers are rejected; configure the underlying executable or script host. REST profiles currently support Ollama, OpenAI-compatible, Anthropic and Gemini completion APIs through PolyPrompt. Automatic REST tool execution is disabled.

The offline descriptor suites do not use the user's home or authenticated agent CLIs. To exercise the real PolyPrompt HTTP adapter, start the committed simulation in another terminal:

```sh
postman simulation run postman/simulations/runners.sim.yaml --port 4491 --output ndjson
ARGUEBOT_SIMULATION_URL=http://127.0.0.1:4491/ollama dotnet run --project src/Test.Automated -c Release -f net10.0
```

When the simulation is not configured, those three cases report explicit skips. Successful local fixture/simulation tests do not establish live-provider compatibility or release readiness. Runtime/simulation logs and real captures belong under ignored `.run/`, never in source control.


The React dashboard is served by the replacement listener after `cd dashboard && npm ci && npm run build` and a server build. For the Vite development server, start the API on 4417, then run `npm run dev` in `dashboard`. The local browser pairs with the current code from its service home. Theme, icon-only transcript/new-run actions, and help text are browser regression requirements.

Dashboard validation uses `npm run i18n:check`, `npm run lint`, `npm run test:ci`, and `npm run test:e2e`. The browser harness starts a separate service on 4420 and stores fixtures/screenshots beneath ignored `.run/`. Run `npx playwright install chromium` once. Component and browser suites have separate discovery patterns. API artifacts regenerate together with `node scripts/generate-api.mjs` from the repository root; the authoritative served document is `/openapi.json`.

Build the solution before setting `ARGUEBOT_CLI_FIXTURE` to the absolute path of `src/Test.Fixtures/bin/Release/net10.0/Test.Fixtures.dll`. The shared suite then verifies capture failures against real child processes, including a malformed process that stays alive and stderr overflow. Missing optional integrations report skips. .NET 10 also renders the actual TUIKit shell headlessly and audits focus and theme controls; library and storage suites also run on .NET 8.

The terminal uses Ctrl+B to choose two or three debate participants, Ctrl+R for moderator controls, Ctrl+A for a follow-up, Ctrl+F for search and archive scope, and Alt+Left/Right for history pages. Ctrl+Q detaches without stopping service-owned work. Profile configuration currently uses the web or console interface. Remaining terminal screen and localization gaps are tracked in the plan.

For real PostgreSQL contract tests, start only the disposable fixtures with `docker compose -f docker/providers/compose.yaml up -d --wait`. Set `ARGUEBOT_TEST_POSTGRESQL` to that fixture's connection string. The checked-in Compose passwords are fake, isolated test credentials. SQLite and PostgreSQL are the only supported providers, per the user's explicit scope change. Existing server-provider databases are not reset or migrated automatically by this test workflow.

The CI workflow runs on every push and pull request. It builds and executes console/xUnit/NUnit on Windows, macOS and Linux, and runs dashboard checks/browser journeys plus API artifact drift on Linux. A Linux integration job also runs PostgreSQL lifecycle/snapshot tests and real PolyPrompt transport against the committed Postman fixture implementation. Benchmark, packaging and full acceptance CI gates remain to be added. See [operations](OPERATIONS.md) for local observability and settings behavior.

Recovery lifecycle and its current limits are documented in [RECOVERY.md](RECOVERY.md). Regenerate the dependency/license inventory after a locked full-solution restore with `node scripts/generate-dependencies.mjs`. The service embeds this inventory in new archives. It records declarations and exact lock hashes, rather than asserting legal approval or redistribution readiness.
