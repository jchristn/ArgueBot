# Replacement runtime development

The C# replacement is under active implementation. The npm entry points still run the existing application. Do not use the replacement as a finished hosted product: full RBAC, remaining recovery/import/retention flows, full dashboard/TUI parity, packaging and acceptance gates remain open in IMPROVEMENTS.md.

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

The terminal uses Ctrl+B to choose two or three debate participants, Ctrl+R for moderator controls, Ctrl+A for a follow-up, Ctrl+F for search and archive scope, and Alt+Left/Right for history pages. Ctrl+Q detaches without stopping service-owned work. Ctrl+K opens administrator recovery and durable job browsing. Ctrl+E opens participant creation, editing and deletion. Its form supports CLI protocols and PolyPrompt REST providers, ordered literal argument rows, endpoint/model/environment references and execution bounds. Empty argument rows remain explicit arguments. Server validation errors remain inline without discarding the draft. Remaining terminal screen and localization gaps are tracked in the plan.

For real PostgreSQL contract tests, start only the disposable fixtures with `docker compose -f docker/providers/compose.yaml up -d --wait`. Set `ARGUEBOT_TEST_POSTGRESQL` to that fixture's connection string. The checked-in Compose passwords are fake, isolated test credentials. SQLite and PostgreSQL are the only supported providers, per the user's explicit scope change. Existing server-provider databases are not reset or migrated automatically by this test workflow.

The CI workflow runs on every push and pull request. It builds and executes console/xUnit/NUnit on Windows, macOS and Linux, and runs dashboard checks/browser journeys plus API artifact drift on Linux. A Linux integration job also runs PostgreSQL lifecycle/snapshot tests and real PolyPrompt transport against the committed Postman fixture implementation. Benchmark, packaging and full acceptance CI gates remain to be added. See [operations](OPERATIONS.md) for local observability and settings behavior.

Recovery lifecycle and its current limits are documented in [RECOVERY.md](RECOVERY.md). Regenerate the dependency/license inventory after a locked full-solution restore with `node scripts/generate-dependencies.mjs`. The service embeds this inventory in new archives. It records declarations and exact lock hashes, rather than asserting legal approval or redistribution readiness.

Streaming persistence batches at 4096 UTF-16 characters or a 250 ms timer by default; settings expose both bounds and require restart. Finalization and interruption flush pending text before the terminal message event. Timer progress does not depend on another runner fragment. Commits preserve surrogate pairs even when callbacks split them. Schema 6 records committed event offsets in UTF-16 code units; events captured before schema 6 need snapshot reconciliation rather than assumed offsets. The shared stream case checks exact Unicode content, fragment replay/offsets, bounded write count, stalled-runner persistence and typing-hold expiry against an injected clock. Countdown and hold expiry use monotonic timestamps. Test-only fake-clock adaptation keeps wall-clock adjustments independent: the pinned [FakeTimeProvider implementation](https://github.com/dotnet/extensions/blob/v10.0.0/src/Libraries/Microsoft.Extensions.TimeProvider.Testing/FakeTimeProvider.cs) derives timestamps from its synthetic UTC clock. This is correctness coverage, not a benchmark result.

Set `ARGUEBOT_TUI_EVIDENCE_DIR` to an isolated artifact directory to save scrubbed headless terminal frames. Runtime CI saves these beside its test results. The form host reserves scrollbar space and derives layout extent without allocating a large dummy cell buffer; tests tab through long participant forms to their action buttons. UI dispatch waits cancel when the TUI detaches.

After a Release solution build, `node scripts/test-cli-surfaces.mjs` starts an isolated SQLite service and checks console participant create/update/list/delete plus archive create/status/list/download/verify/delete and persisted operation browsing. Its fictional executable is never run. The script stops its service and removes only its own temporary home; all three runtime CI jobs run it.

Complete archives now restore between SQLite and PostgreSQL. The shared PostgreSQL case creates and drops only its own generated fixture database, verifies nonempty-target refusal and injects a final-publication failure to verify retained staging. Integration CI also runs the real CLI/HTTP flow against a separate empty fixture database. The optional ARGUEBOT_TEST_CLI_POSTGRESQL_REFERENCE hook in the CLI smoke accepts only a declared loopback arguebot_ fixture database.

Local auto-attachment checks an opaque path-bound home identity and a fresh HMAC process proof before user credentials are sent. The proof includes the actual listener port to prevent replay/proxying from a differently configured port. CLI commands share runtime/cli-session.json under owner permissions and an exclusive refresh lock; every invocation revalidates the current principal, and expired/revoked sessions enroll again. TUI/browser sessions remain separate. This local process proof is not a user login scheme, remote identity federation or a substitute for hosted TLS.

Security audit inspection is available at `/dashboard/audit`, `arguebot audit list [OPERATION] [PAGE]`, and Ctrl+J in the terminal. Tenant administrators inspect their tenant; system administrators additionally inspect anonymous/platform events. The application exposes no audit edit/delete operation. Issuance, revocation and HTTP denials are recorded independently of request capture. Direct privileged database access is outside this immutability guarantee. Full hosted credential schemes and scoped RBAC remain in progress.

Generation attempt inspection is available through the workspace, `debate attempts CONVERSATION RUN [PAGE]`, and Ctrl+G in the TUI. The CLI also accepts a typed run request from stdin (`debate start CONVERSATION -`) or `--file PATH`, preserving explicit round/countdown settings and caller idempotency keys. It still accepts ordered profile identifiers as command arguments. The CLI smoke executes real fixture processes; browser debate coverage reuses the committed Postman Ollama fixture through the actual PolyPrompt transport, without provider credentials. See [Execution and recovery](EXECUTION.md) for transaction boundaries, directive retry semantics, frozen questions, context limits and remaining provenance work.

Build native clients and service together for attachment protocol 2. GET message history returns a bounded `MessagePage`; the internal client's full-history helper hydrates previews through individual message reads. Web and TUI polling use bounded pages. Ctrl+L in the TUI pages history and reads full messages, preserving Backspace. `conversations show ID --jsonl` and `conversations export ID` stream complete messages. The browser journey checks paging during concurrent appends and full export of a long Unicode message outside the visible page.

Replay validation covers both supported databases, contiguous age/count pruning, permanent history, background maintenance, expired/future cursors, native live streaming and cancellation. Synthetic native/browser streams cover split UTF-8, CR/LF framing, duplicate events, gaps, mismatched conversation IDs, revocation, invalid encoding and oversized frames. Transport limits are generated into assets/transport.json and OpenAPI from ServiceProtocol; CI checks artifact drift. The browser recovery journey forces an expired cursor and requires fresh snapshots before resubscription.

Tenant identity evidence covers SQLite/PostgreSQL creation, unique tenant emails, protected owner, administrator flag boundaries, current-role resolution, password rotation/disablement and revision conflicts. Native HTTP checks cover encoded email search and active SSE revocation; browser checks cover masked forms, viewer direct-API denials, session invalidation and diagnostic redaction. Ctrl+U forms are exercised through TUIKit headless rendering. See [identity behavior](IDENTITY.md).

HTTPS fixtures generate disposable two-day loopback certificates and never modify system trust. Browser automation accepts only its disposable fixture context certificate. Native shared tests exercise actual pinned TLS startup, session carriers, live revocation, and negative pin/home/port/name/expiry cases. Console smoke covers default HTTP pairing, refusal of HTTP password exchange, restart into HTTPS and user login. Node-based CLI/browser checks invoke the committed Test.Fixtures certificate mode; both it and the shared C# fixture use CertificateRequest without external certificate tooling.

Execution identity checks run on SQLite and PostgreSQL: session revocation leaves accepted generation active, role removal interrupts it, stale-context resume creates no new attempt, account disablement interrupts a subsequent active generation, and denial provenance is retained in the audit stream. Fixture cleanup stops paused runs before deletion. Lifecycle cleanup captures the verified owned child even if readiness fails after descriptor publication, preventing a cleanup file-lock error from hiding the startup failure.

The stalled-stream fixture signals readiness after its first fragment is queued. This makes fake-clock advancement deterministic: advancing before the callback queued text could otherwise fire an empty flush and leave the next timer waiting on an unadvanced fake clock. Hosted Windows adapter testing exposed this fixture race.

The scoped-authorization truth table is a pure shared descriptor, independent of live providers. It checks order-independent explicit deny, Write requiring all mutations, Execute/Admin separation, tenant/collection inheritance boundaries, case-sensitive resource IDs, ownership narrowing, inactive rules, malformed rule refusal, grant caps and credential owner ceilings. See [authorization integration status](AUTHORIZATION.md).

Password storage validation: 51 shared console descriptors passed with real PostgreSQL and PolyPrompt fixtures. A schema-10 SQLite source upgrades to salted PBKDF2 verifiers while preserving passwords, account IDs and existing sessions; equal inputs receive distinct salts, stored verifiers fail login, and a protected pre-upgrade backup is retained. NUnit and xUnit pass on .NET 8 and .NET 10; all 15 HTTPS browser journeys and 22 console assertions pass. OpenAPI syntax lint passes.

Windows CI subsequently exposed another StreamCommits deadline cancellation in the .NET 10 xUnit adapter. The fixture now identifies burst persistence, stalled-stream checkpoint and monotonic-window timeout phases and allows a bounded 30-second scheduling budget instead of five seconds. Content, replay offsets and batching assertions remain unchanged. Both local xUnit runtime targets pass; hosted verification determines whether a deeper scheduling issue remains.
