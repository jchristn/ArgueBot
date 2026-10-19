# ArgueBot product and repository improvement plan

Status: implementation in progress. The legacy application remains runnable. Work packages are not complete until their full gates pass.

Verified foundation: .NET solution builds with zero warnings; shared Touchstone descriptors run through console/xUnit/NUnit; SQLite history, receipts, tenant/viewer checks, run snapshots, interrupted recovery, exclusive home ownership and engine interrupt/resume have local test coverage. A local HTTP smoke journey exercised pairing, profiles, a two-participant fixture debate, history and session revocation. Real PolyPrompt transport passed committed Postman simulations for success, rate limiting and broken streams. See docs/DEVELOPMENT.md. Full provider, hosted security, interface, operations and release gates remain open.

Additional verified slices: schema-11 salted password-verifier migration preserves account passwords and sessions, rejects verifier replay, and passes the 51-check shared suite including real PostgreSQL and PolyPrompt fixtures. real PostgreSQL lifecycle tests pass alongside SQLite; the generated contract covers 45 implemented operations; the React dashboard has browser journeys for conversation persistence, icon controls, responsive themes, request inspection, API execution and literal argument editing. AES-GCM session nonce/tamper/expiry/rotation/revocation cases pass. Native OTLP export has reached Prometheus, Tempo and Loki in the provisioned local stack. Hosted CI passed dashboard/browser checks; an initial runtime restore exposed a private-cache TUIKit hash mismatch, now corrected against the published package with an isolated repository cache. TUIKit focus/theme, concurrent service startup/pairing/detachment/shutdown, durable moderator receipts and portable snapshot corruption/rollback tests pass locally; PostgreSQL snapshots restore into SQLite. All milestone checkboxes remain open until the complete gates pass.

Prepared October 19, 2026 against commit `e4a2f73`. The intended result is one persistent conversation product with a web dashboard, a C# TUIKit terminal application, scriptable console commands, and interchangeable CLI-harness and PolyPrompt model runners.

## 1. Product direction and delivery rules

ArgueBot should let a user open a conversation in the terminal, watch it in a browser, close both interfaces, and return to the same history later. A conversation contains questions, debate runs, moderator interventions, follow-ups, and outcomes. The service owns that state and all execution. Interfaces display it and submit commands.

The rewrite replaces both existing TypeScript debate loops with one C# engine. TUIKit and PolyPrompt are consumed as pinned NuGet dependencies, using their sibling source repositories for reference rather than project references. Existing Claude Code, Codex, and Gemini CLI integrations remain supported. PolyPrompt adds local and remote completion endpoints without pretending that a completion API supplies the agent behavior of a coding CLI.

Default installation is local-first: application data lives in `~/.arguebot`, SQLite needs no separate database process, and the listener binds to `127.0.0.1`. Local-first does not remove authentication, authorization, tenant isolation, or the other backend requirements. Hosted deployments use the same domain model with explicit identities and permissions.

Implementation proceeds in dependency order. Each work package below has a primary implementation area, concrete deliverables, and an exit gate. Work is complete only when its code, meaningful tests, user documentation, API artifacts, and relevant changelog entry agree. Checkboxes track execution, not intent. A partially implemented milestone must remain incomplete.

- [ ] Keep the existing implementation runnable until the replacement passes parity and migration gates.
- [ ] Do not change any product version, prerelease label, package version field, release tag, or versioned changelog heading without an explicit user request for that version change. Use an `Unreleased` changelog section for implementation work.
- [ ] Do not publish packages, submit store entries, create external accounts, or perform a production migration as a side effect of building the replacement. Prepare and verify artifacts first; track release credentials and external review gates separately.
- [ ] Record changes to this plan with the reason, affected acceptance criteria, and evidence. Do not silently turn mandatory requirements into optional backlog items.

## 2. Current behavior and gaps

The repository already contains useful prompt construction, consensus detection, streaming CLI adapters, transcript export, intervention controls, and a browser interface. Those behaviors are migration inputs. They must be characterized before their implementations are removed.

| Area | Current evidence | Required change |
| --- | --- | --- |
| Console execution | `src/orchestrator.ts`, `src/input.ts`, `src/ui.ts` own an interactive loop | Thin TUIKit and noninteractive clients of the shared service |
| Web execution | `src/web/session.ts` owns a separate loop and supports two or three participants | One engine with the same participant capabilities in every surface |
| Persistence | `src/web/server.ts` stores sessions in a process-local `Map` | Durable conversations, run checkpoints, configuration, and event replay |
| Agent identity | `AgentName` is a fixed Claude/Codex/Gemini union | User-defined participant profiles referencing runner configurations |
| Model access | `src/agents.ts` launches installed CLI harnesses | Retain harness support and add PolyPrompt-backed REST endpoints |
| Web application | `web/` contains plain HTML, CSS, and JavaScript | Required React/Vite dashboard under `dashboard/`, with a shared API client |
| Repository | No checked-in automated test suite, release pipeline, or `CHANGELOG.md` identified in the inspected tree | Full build, test, documentation, operations, and distribution structure |
| README accuracy | README says both interfaces run the same engine, although they currently have separate orchestration implementations | Correct the description during implementation and document the actual shared-service design at cutover |

The existing web theme toggle, icon-only transcript download and new-debate actions, and hover help are explicit regression requirements. A redesign must retain them. Existing CLI flags, script entry points, prompt semantics, cancellation behavior, and export formatting need a written compatibility inventory, not assumptions based on the new design.

## 3. Requirements traceability and scope

The normative source is `~/Code/Agents/requirements`. The table lists every document in that directory and where this plan applies it. Those files should be re-read at implementation time because they can change independently of ArgueBot. A checked-in traceability record will capture source hashes and the reviewed date so future contributors can distinguish a requirement change from an implementation defect.

| Requirement document | Application in this plan | Delivery evidence |
| --- | --- | --- |
| `README.md` | Requirements directory index | Source inventory in P0 |
| `REPOSITORY_REQUIREMENTS.md` | Source layout, MIT license, API docs, Postman, Docker conventions | P0, P4, P9, P10 repository checks |
| `CODE_STYLE.md` | Strict C# conventions, public documentation, cancellation, disposal | P0 analyzer configuration and every C# review |
| `VERSIONING.md` | Human-controlled version changes; approved pre-1.0 releases labeled alpha | P0 release guard and P10 preflight |
| `BACKEND_ARCHITECTURE.md` | Watson 7.1, typed routes/context, PrettyId, structural tenancy, SQLite and PostgreSQL (user-directed provider scope) | P1 through P4 and provider matrix tests |
| `BACKEND_TEST_ARCHITECTURE.md` | Touchstone shared descriptors and automated/xUnit/NUnit runners | P0 and P8 |
| `AUTHENTICATION.md` | Authentication, authorization, accounting, scoped credentials, revocable sessions | P2, P4, security journeys in P8 |
| `FRONTEND_ARCHITECTURE.md` | React 19, Vite 6, Router 7 baseline, shared fetch client, contexts, route structure | P6 dashboard implementation and build checks |
| `DASHBOARD_STYLE_AND_USABILITY.md` | Complete route inventory, designed forms/tables, operational surfaces, responsive visual QA | P6 route acceptance matrix |
| `I18N.md` | Shared locale registry/catalogs, formatting, translated help, RTL/pseudo-locales | P5, P6, P8 localization checks |
| `TELEMETRY_REQUIREMENTS.md` | Watson plus application metrics/traces, logs, provisioned observability stack | P7 telemetry and operations checks |
| `EXAMPLE_APPLICATIONS.md` | Select concrete examples by feature and verify actual APIs before using them | P0 reference map; implementation reviews |
| `INSTALLERS.md` | Manifest-driven Publisher, self-contained binaries, OS packaging and package managers | P10 artifact and install verification |
| `SIMULATED_USER_TESTING.md` | Clean-instance journeys, full surface inventory, evidence, user-triaged findings | P11 report and separate finding decisions |
| `BENCHMARKING.md` | Isolated public-surface harness, datasets, baselines, reports, deterministic CI gate | P8 benchmark deliverables |
| `WRITING_DOCUMENTS.md` | Human review, developed prose, precise claims, no em-dashes | P9 editorial check and final document review |

### Compliance decisions and boundaries

SQLite and PostgreSQL are the supported release providers. On October 19, 2026 the user explicitly narrowed the scope: "You only need sqlite and postgres. no need for mysql and sql server." This instruction overrides the four-provider baseline in `BACKEND_ARCHITECTURE.md`; it is an intentional requirements exception, not deferred implementation. Remove the other drivers, packages, configuration options, fixtures and tests. Both supported providers still require migrations, backup/recovery and shared behavioral evidence.

The frontend standard requires React/Vite and a complete operational dashboard. Keeping the existing plain-JavaScript frontend indefinitely would not satisfy it. Authentication, administration, request history, API Explorer, settings, and observability therefore belong in the product plan alongside conversation navigation. No first-pass dashboard is presented as complete with placeholder core workflows.

The authentication reference uses some legacy GUID terminology and examples, while the backend standard requires PrettyId string identifiers. Use prefixed PrettyId strings up to 64 characters for ArgueBot entities and carry those identifiers consistently through identity, API, and storage. Preserve external provider identifiers as external values. Global built-in roles are an explicit, protected exception to tenant ownership; global schema bookkeeping is another documented exception.

The I18N and benchmarking documents require checks on every push to every branch and every pull request. That cadence is stricter than the branch-filtered example in the backend test document, so CI follows the stricter prose requirement. Likewise, use classic `using` blocks, no partial classes, and no tuples, satisfying the stricter backend style rules.

Docker healthchecks use the required five-second interval, two retries, a timeout of at most five seconds, and cURL against `127.0.0.1`. Docker's native `retries` setting describes consecutive failures, not two successful probes before recovery. If the product promises the reference's two-success online transition, implement that hysteresis in the health aggregation layer and test it; do not claim Docker itself supplies it.

MCP, public SDKs, external identity federation, automatic model-tool execution, and conversation branching are future extensions. The initial rewrite does not introduce those surfaces. If introduced, their documentation, authorization, tests, packaging, and traceability become mandatory in the same change. An MCP server requires `MCP_API.md`; an SDK requires `sdk/{language}/README.md` and its own test harness.

## 4. Target repository and dependency structure

The service is the only writer and runner owner for a given application home. Both interfaces use its typed HTTP contract and durable event feed. The TUI can start or attach to the local service, following the lifecycle approach used in S3Drive, but it cannot open the database and independently launch debates. That boundary prevents duplicate execution when web and terminal are open together.

```text
Web dashboard       TUIKit TUI       Console commands
       \                 |                 /
        +------ authenticated service ----+
                         |
              Conversation/debate engine
                /         |          \
       Database drivers   |        Runner boundary
                          |         /          \
                    Durable events  CLI stdio  PolyPrompt REST
```

Proposed repository layout:

```text
src/
  ArgueBot.sln
  ArgueBot.Core/             domain, state machine, services, database boundaries
    Database/
      Interfaces/
      Sqlite/{Implementations,Queries}/
      Postgresql/{Implementations,Queries}/
  ArgueBot.Runners/          process adapters and PolyPrompt adapters
  ArgueBot.Server/           Watson host, routes, composition, background work
  ArgueBot.Client/           typed internal HTTP/event client for console surfaces
  ArgueBot.Tui/              TUIKit shell, screens, forms, controllers
  ArgueBot.Cli/              scripting commands and text/JSON/JSONL output
  ArgueBot.Publisher/        testable manifest-driven packaging
  Test.Shared/              Touchstone descriptors and suite inventory
  Test.Automated/           Touchstone console runner
  Test.Xunit/               xUnit adapter
  Test.Nunit/               NUnit adapter
  Test.Fixtures/            executable deterministic CLI harness fixture
  Test.Benchmark/           separate ArgueBot.Benchmark executable
dashboard/                  React/Vite app and browser tests
assets/
  i18n/                     canonical locale registry, catalogs, glossary
  postman/                  documented collection and safe environment templates
  grafana/                  provisioned dashboard JSON
docker/                     Dockerfiles, compose.yaml, settings, telemetry, helpers
packaging/                  in-repository channel manifests and templates
benchmarks/                 datasets, isolated stack, baselines, curated results
docs/                       architecture, operations, migration, user/test reports
.github/workflows/          checks and separate release workflow
```

`ArgueBot.Core` must not reference Watson, TUIKit, or frontend code. The runner project references Core contracts; the server composes Core, database drivers, and runner implementations. The internal client contains transport DTOs and event handling without depending on the server executable. Use interfaces at replaceable boundaries rather than creating an interface for every class.

### Required implementation conventions

Enable nullable reference types, explicit usings, analyzers, deterministic builds, and warnings-as-errors for new projects. Use block namespaces with alphabetized System/Microsoft usings first inside the namespace, explicit types instead of `var`, one type per file, `_PascalCase` private members, no tuples, and no partial classes. Document all public types, members, constructors, defaults, bounds, exceptions, nullability, and thread-safety guarantees; do not add XML documentation to private members.

Cancellable asynchronous methods accept a `CancellationToken` or use the documented class-owned token source. Check cancellation at meaningful boundaries and use `ConfigureAwait(false)` in library and server code. UI continuations marshal through TUIKit's documented dispatcher rather than relying on arbitrary continuation threads. Validate arguments and setters, use specific exceptions or typed failure results, dispose resources reliably, and keep `Console.Write*` out of libraries and shared tests. Provide cancellable async enumeration companions where the style standard requires them. Configurable bounds belong in validated settings rather than scattered constants.

JSON request and response contracts use typed DTOs with a centralized serialization policy. Follow the backend JSON guidance when choosing the serializer; do not use `JsonElement` property probing for fixed contracts. PolyPrompt's native raw payload types remain inside integration boundaries. Package APIs and signatures must be verified from the actual pinned package/source, never invented from examples or guessed names.

Target .NET 10 for the new applications and publisher. Multi-target reusable Core, Runners, Client, and shared test code to .NET 8 and .NET 10 where dependency compatibility permits, and document any narrower target before committing its public contract. Pin the SDK in `global.json`, dependency versions in shared build/package settings and lockfiles, and the dashboard toolchain in its manifest/lockfile. The inspected sibling sources currently declare TUIKit 1.10.0 and PolyPrompt 3.4.0; treat them as candidates to restore and validate in P0, not an assertion about the latest published packages.

## 5. Persistent application home

Resolve the default home from the operating-system user profile directory, never the current working directory or a literal shell `~`. On Windows the same default is `%USERPROFILE%\.arguebot`. `--home` overrides `ARGUEBOT_HOME`, which overrides the default. A remote client keeps its own preferences locally, while conversations and runner configuration live on the selected server. Clearly show which machine owns data and executes tools.

```text
~/.arguebot/
  arguebot.json              typed service/bootstrap settings
  arguebot.db                default SQLite database
  arguebot.db-wal            SQLite-managed while open
  arguebot.db-shm            SQLite-managed while open
  runtime/                   instance lock, discovery metadata, local connection data
  logs/                      rotating structured logs
  crash-logs/                scrubbed diagnostic reports
  backups/                   consistent, versioned backup bundles
  exports/                   user-requested Markdown/JSON exports
  tmp/                       bounded process input/capture scratch space
```

Service settings belong in `arguebot.json`: listener, database driver selection/connection references, logging, telemetry, retention, limits, and local bootstrap policy. Provider configurations, endpoint profiles, harness profiles, participant presets, conversation state, and preferences belong in relational tables. A field has one authoritative store. Settings APIs update the JSON file atomically; resource APIs update the database transactionally.

- [ ] Create the home idempotently, with user-only directory/file permissions where supported and equivalent Windows ACLs. Preserve paths containing spaces and non-ASCII characters.
- [ ] Add `ArgueBotPaths`, validated settings classes, schema/version metadata, atomic settings writes, a last-known-good recovery path, and startup diagnostics with actionable paths but no secrets.
- [ ] Acquire an OS-backed exclusive instance lock before database migrations or runner startup. Do not treat the presence of an old PID file as proof of a live process.
- [ ] Discover a local server using protected runtime metadata and validate health, process ownership, and protocol compatibility before attaching. Two simultaneous starts must converge on one service or return a clear error.
- [ ] Apply defaults, file settings, environment overrides, and explicit command-line service overrides in a documented order. Expose effective source and live/restart-required metadata without leaking values of secret overrides.
- [ ] Reject incompatible future database schemas without modifying them. Back up before upgrades; record ordered, repeatable migrations and their outcomes in the database.
- [ ] Use foreign keys, WAL on suitable local filesystems, a configurable busy timeout, short transactions, serialized writes through `SemaphoreSlim`, and bounded retry on lock contention. Reject unsupported shared/network-filesystem SQLite deployments with guidance to use a server database.
- [ ] Never hold a database transaction open while waiting for a CLI process or model response. Capture final messages and checkpoint changes transactionally after work completes.
- [ ] Provide backup/list/verify/restore commands and UI workflows. Use SQLite's supported online backup facility or a controlled offline backup, not a copy of a live `.db` without its WAL state.
- [ ] Include database/schema version, settings, manifest, checksums, and dependency references in backups. Exclude secret values by default; document how keychain/environment references are restored and how an explicitly encrypted secret-inclusive backup is handled.
- [ ] Restore only after draining work and validating the bundle into a temporary location. Keep a rollback copy, reject path traversal/corrupt checksums, and report missing credentials after restore.
- [ ] Define retention independently for request history, audit, process captures, logs, replay events, and conversations. Do not silently expire conversation history. Archive is reversible; permanent deletion is a distinct authorized action.

Storage failures must not produce false success. A disk-full or failed commit stops acceptance of new work, records a useful failure where possible, and keeps the UI's unsaved state visible. Request-history capture is the documented best-effort exception and must not break an otherwise successful response.

## 6. Domain model and relational schema

A conversation is the durable container. A debate run is an execution episode with frozen participants, runner settings, prompt version, selected context, round limit, and outcome. Follow-up questions are message exchanges in that same conversation, not a terminal state that prevents later work. A subsequent debate is a new run with explicit context selection.

Every tenant-owned table includes `TenantId`; user-owned tables also include `UserId`. Use string IDs up to 64 characters, UTC created/updated timestamps, and active flags as required. Use protected flags for security-critical records. Primary identifiers come from PrettyId with centrally defined prefixes such as `cnv_`, `run_`, `msg_`, `ep_`, `hrn_`, `par_`, and `evt_`. Foreign keys enforce matching tenant scope, including composite ownership keys where needed.

| Entity/table family | Structured fields and relationships |
| --- | --- |
| Identity and access | Tenants, users/admins, credentials, auth sessions, roles, permissions, role-permission maps, scoped user assignments, credential-scope assignments, audit denials/events |
| Provider configurations | Provider kind, display name, enabled state, capability metadata; owned by tenant and optionally user |
| REST endpoints | Provider reference, validated base URI, default model, timeouts, concurrency/rate limits, auth mechanism and secret references; typed provider-specific child settings |
| Harness configurations | Executable, protocol adapter, input mode, working-directory policy, timeout, output bounds, capability flags; ordered argument rows and environment-variable reference rows |
| Participant profiles | Display identity, runner kind/reference, default model/role, supported options, enabled state; no hard-coded three-name enum |
| Conversations | Title, owner, pinned/archived timestamps, created/updated/last-message timestamps, revision, optional future fork provenance |
| Conversation participants | Ordered participant references and conversation-specific nonsecret overrides |
| Debate runs | Conversation reference, state/outcome, current round and speaker index, limits, pause/stop intent, checkpoint revision, run ownership/lease metadata |
| Run participants and context | Immutable nonsecret configuration snapshots in typed columns/child tables; source message IDs and summary references with stable ordering |
| Messages and recipients | Conversation/run references, monotonic sequence, speaker/profile snapshot, message kind, content, visibility, generation state, timing, error code, interruption flags, intended directive recipient |
| Generation attempts | Message/run references, provider/harness snapshot reference, attempt number, exit/status code, usage if reported, duration, completion/failure reason |
| Durable events | Conversation sequence, event kind, entity reference, command correlation, timestamp, typed payload fields/child structures needed for replay |
| Command receipts | Principal/conversation scope, idempotency key, request digest, accepted result, execution reference, expiry policy |
| User preferences/read state | Locale/theme, per-client active selection, per-conversation last-read sequence, draft text/mode/recipient and optimistic revision |
| Operations | Request history, settings-change audit, backup manifests, migration history and health summaries where retention is required |

Known application properties must not be hidden inside serialized `state_json`, `record_json`, generic metadata bags, or whole-object blobs. Ordered arguments, environment mappings, participants, grants, settings snapshots, and recipients get child tables. Raw third-party diagnostic payloads are an explicitly unmanaged exception with clear names, bounded size, redaction, retention, and no authorization or normal filtering logic inside that JSON.

Create tenant-scoped indexes for recent conversations, archive/pin ordering, message/event sequence, run state, request-history time ranges, endpoint names, and command keys. Scope uniqueness to the tenant or owner as appropriate; do not make conversation titles globally unique. Specify cascade and soft-delete behavior for every relationship. Historical run snapshots survive endpoint/profile edits; deleting a configuration that active work references returns a conflict or requires explicit disable-and-drain behavior.

Implement `DatabaseSettings`, `DatabaseTypeEnum`, `DatabaseDriverBase`, `DatabaseDriverFactory`, and `SchemaMigration`. Expose domain methods such as `IConversationMethods`, `IRunMethods`, `IEndpointMethods`, `IHarnessMethods`, `IMessageMethods`, `IEventMethods`, and identity/history methods. Each provider has `Implementations/` and `Queries/`, uses parameterized provider-aware SQL, and runs the same isolation, lifecycle, pagination, migration, and restart tests. SQLite can use FTS5 for message search; the other providers must preserve documented search semantics through their supported implementations.

## 7. Runner configuration and execution

The runner boundary exposes capability discovery, validation, streaming execution, cancellation, and a final typed result. Events distinguish text deltas, status, usage, completion, tool requests where relevant, and errors. The UI works with those capabilities instead of assuming every participant can access a filesystem or accept a particular model option.

### CLI harnesses and stdio

Preserve dedicated adapters for Claude Code, Codex, and Gemini CLI. Add a generic plain-text or documented JSONL harness configuration for other executable integrations. Stdio capture means launching and supervising a process, reading stdout and stderr concurrently, and supplying input through its declared protocol. It does not imply MCP support or a persistent agent session protocol.

- [ ] Store executable and ordered arguments separately. Use direct process execution with `UseShellExecute = false` and the platform's structured argument facility. Never concatenate user prompts into shell commands.
- [ ] Prefer stdin or a protected temporary input file for prompt content. Detect Windows `.cmd`/`.bat` shims and resolve a supported underlying executable/script host with fixed arguments; never pass prompt content through `cmd.exe` parsing.
- [ ] Where a harness requires a prompt argument, use only verified direct-executable argument handling and document the process-list privacy limitation. Unsupported shell-only wrappers fail configuration validation rather than silently enabling interpolation.
- [ ] Drain stdout and stderr concurrently to prevent pipe deadlocks. Preserve UTF-8 decoder state and partial JSONL records across reads; handle CRLF, ANSI sequences, malformed events, nonzero exits, and a final snapshot that repeats streamed text.
- [ ] Bound captured bytes, line length, event count, idle time, and total runtime. Report truncation rather than silently dropping output or exhausting memory.
- [ ] Resolve a validated working directory and declared environment references per run. Show that the directory belongs to the server machine. Missing executables and authentication need separate diagnostics.
- [ ] Keep CLI credential discovery inside the CLI integration. Do not scrape CLI OAuth credentials into PolyPrompt provider profiles or overwrite existing CLI authentication files.
- [ ] Support graceful interrupt followed by bounded process-tree termination on Windows, Linux, and macOS. Mark partial responses interrupted, retain useful text, and test orphan-child cleanup.
- [ ] Treat no-confirmation/tool permission mode as a per-harness capability and explicit saved setting. Preserve the existing YOLO behavior only when that harness supports it, and visibly show its active state.
- [ ] Ship an executable fixture supporting slow output, fragmented records, repeated snapshots, stderr floods, hang, cancellation, nonzero exit, and child processes. Tests use fixtures rather than real authenticated agent CLIs by default.

### PolyPrompt-backed REST endpoints

Use the published PolyPrompt package for supported completion providers. Start with local Ollama, OpenAI-compatible endpoints where the client supports the configured API, Anthropic, and Gemini, then cover the other completion-capable providers advertised by the pinned package. Endpoint creation is capability-driven: embedding-only or classification-only providers cannot be selected as debaters. Unsupported native options remain unavailable with an explanation.

- [ ] Provide designed forms for provider, endpoint URL when configurable, model, authentication references, timeouts, generation settings, concurrency, and supported provider-specific options.
- [ ] Honor provider-fixed endpoints and actual package capability limits. Do not claim an arbitrary REST URL is compatible without a protocol adapter and a successful validation/test path.
- [ ] Add separate connectivity validation, model discovery where supported, and an explicit small test-completion action. Explain that a test completion can use provider quota; passive validation never silently generates paid completions.
- [ ] Map the same authorized conversation context to native message roles. Apply the configured participant role/system prompt and preserve directive recipient rules.
- [ ] Normalize stream chunks, reasoning/status separation where available, completion reason, usage, errors, cancellation, and timeouts. Do not infer a successful generation merely because some text arrived.
- [ ] Respect `Retry-After`, configure bounded backoff, and distinguish pre-response retries from generation retries. Never replay an uncertain or partially streamed request automatically, since it may duplicate work or spend.
- [ ] Normalize PolyPrompt HTTP failure results and cancellation exceptions explicitly. Test 401/403, 404 model errors, 409, 429, 5xx, broken streams, and an upstream becoming unavailable mid-turn.
- [ ] Keep API keys, headers, cloud credentials, and tokens in environment or OS credential-store references. If encrypted database storage is offered, use authenticated encryption with keys outside the database and define rotation/export/restore behavior. APIs return set/unset and redacted previews, never recoverable secrets.
- [ ] Disable or redact library call recording before storing diagnostics. Redact prompt bodies and tool content according to explicit capture settings; secret headers are always redacted.

PolyPrompt returns tool requests but does not execute them, and it does not store conversation history. The first REST runner release supports conversational debate. Automatic tools require a separate application-owned execution policy, approval model, result loop, cancellation, and tests. A model asking to run a command must not cause the server to execute one through the generic harness adapter.

## 8. Shared conversation and debate engine

The engine takes validated commands and emits durable semantic events. It must not depend on a browser connection, terminal input loop, or subscriber lifetime. Run execution uses a service-owned cancellation token; disconnecting an HTTP request or closing the TUI cancels that request/subscription, not an accepted debate. Stop and interrupt are explicit commands.

Support two or three ordered participants initially on every surface, with a validated configurable maximum for future expansion. A run can progress through queued, agent-turn, intervention-window, paused, completed, cancelled, failed, and interrupted states. Generation attempts distinguish pending, streaming, completed, interrupted, and failed. An interrupted run becomes paused/recoverable; it never silently reruns an uncertain CLI operation after restart.

- [ ] Extract characterization cases for opening/rebuttal prompts, moderator notes, private steering, consensus markers, follow-ups, round extension, timers, and transcript export before porting.
- [ ] Write a transition table with legal commands, preconditions, durable changes, emitted events, and error codes. Use an injected clock for deterministic countdown tests.
- [ ] Serialize mutations per conversation; permit bounded concurrency across conversations. A run lease/revision prevents duplicate workers from advancing the same run.
- [ ] Commit command acceptance, state changes, and associated durable events atomically. Same idempotency key and same payload return the same receipt; same key with a different payload returns 409.
- [ ] Persist checkpoints around every attempt, speaker advance, intervention, and run outcome. On startup reconcile unfinished work, remove stale holds, identify interrupted attempts, and present explicit continue/retry choices.
- [ ] Persist streamed content in bounded batches with event ordering and a configurable flush interval. Define the maximum loss window for uncommitted chunks. Finalized messages must survive an immediate restart.
- [ ] Give every replayable chunk/event a stable sequence and message offset or revision. Clients deduplicate repeats and recover missing data from snapshots. Never stream an event as durable before its required commit succeeds.
- [ ] Keep original transcript text unchanged; derive model context separately. Record selected message IDs, prompt version, participant snapshots, optional summary boundaries, and estimated budget. Make exclusions visible.
- [ ] Add context-budget enforcement and cancellable summarization with recorded provenance. Do not silently drop early messages or leak private directives into another participant's context or export.
- [ ] Define steering as a one-recipient directive consumed by the intended next speaker. Preserve owner visibility and auditability while enforcing exclusions in prompts and recipient-filtered streams/exports.
- [ ] Implement per-client composer holds with expiry. If two clients are typing, one client's release must not clear another client's hold; disconnects cannot freeze the countdown indefinitely.
- [ ] Reject conflicting commands with current state/revision and actionable recovery information. Two clients starting the same run, double-clicking Send, or extending an already-completed run cannot produce duplicate turns.
- [ ] Report consensus as an agent-declared result, preserving the existing marker behavior. Do not describe agreement as proof of factual correctness. Record who declared it and the supporting turn.

Conversation management includes create, rename, pin, archive/unarchive, search, pagination, export, and permanent delete with dependency rules. Switching threads never stops background work. Active work must be stopped/drained before deletion or deletion must return a documented conflict. Follow-ups address an explicit participant; restarting debate within a conversation creates a new run and snapshots the selected history.

## 9. Service, authentication, and API contract

Use Watson 7.1 as the HTTP stack, a thin `Program.cs`, an instance server/bootstrapper, per-feature route registrars, typed DTOs, typed `RequestContext`, and PrettyId helpers. Register `Preflight`, authentication, and `PostRouting` hooks; use route metadata for OpenAPI and HTTP telemetry. Default service port can remain 4317 for compatibility, but observability collector ports must avoid colliding with it.

Before implementation, write the versioned OpenAPI contract, `REST_API.md`, and the documented Postman collection under `assets/postman/`. The table below is the required resource inventory, not permission to omit individual methods from those artifacts. Each route needs exact request/response examples, authentication, permissions, error codes, validation, pagination/filter/sort rules, and cancellation semantics.

| Route family | Required operations and behavior |
| --- | --- |
| `/health`, readiness, version, `/openapi.json` | Anonymous minimal health where appropriate; authenticated diagnostic detail; readiness checks schema/database and service state |
| `/v1.0/api/auth/*` | Bootstrap, login/exchange, refresh where supported, logout/revoke, current principal and effective permissions |
| `/v1.0/api/conversations` | Create, enumerate/search with counts and filters; read/update/delete by ID; archive/pin fields and optimistic revisions |
| `/v1.0/api/conversations/{id}/messages` | Paginated history, submit note/directive/follow-up with explicit recipient and idempotency key |
| `/v1.0/api/conversations/{id}/runs` | Create run from selected context; enumerate/read; state-specific pause/resume/interrupt/stop/extend commands |
| `/v1.0/api/conversations/{id}/events` | Authenticated SSE with durable event IDs, resume cursor, heartbeat, authorization recheck, retention-gap recovery |
| `/v1.0/api/conversations/{id}/export` | Authorized Markdown and versioned JSON export with correct content type, filename, visibility, and streaming limits |
| `/v1.0/api/providers`, `/endpoints`, `/harnesses`, `/participants` | Configuration lifecycle, validation, capabilities, model listing where supported, explicit connection/test-generation actions |
| `/v1.0/api/preferences`, drafts/read-state | User/client preferences and revisioned per-conversation drafts/read cursors |
| `/v1.0/api/settings`, server info, restart | Masked settings, effective sources, field validation, live/restart metadata, audited admin restart and recovery |
| `/v1.0/api/request-history` | Enumerate, detail, summary, delete/clear with filtering and retention; empty results when capture disabled |
| `/v1.0/api/health`, activity, logs, diagnostics | Authorized operational summaries, endpoint/runner state, recent failures and redacted log access |
| `/v1.0/api/backups` | Create/list/verify/download/delete and controlled restore; admin-only and bounded filesystem access |
| `/v1.0/api/tenants`, users, credentials, roles, permissions, assignments, audit | Complete applicable administration lifecycle, protected-record rules, credential rotation/revocation and effective permission inspection |

Use a consistent paged response with `items`, `pageNumber`, `pageSize`, and `totalCount`, plus stable ordering and optional continuation metadata. Filter/search/sort on the backend, validate fields against an allowlist, and add database indexes. Errors carry stable codes, safe messages, field errors, request/correlation IDs, and retry guidance. Distinguish 400/401/403/404/409/429 and server failures. Limit request bodies, connection counts, exports, replay ranges, and long-running operations.

SSE remains appropriate for one-way generation updates. Authenticate it with the same principal model as REST. Use a fetch-based SSE reader in the shared clients when bearer headers are needed; native browser EventSource cannot set arbitrary authorization headers. Never put tokens in URLs. Validate resume cursors against conversation and tenant, deliver replay and live updates without a race, detect retention gaps, and close/reconnect on session revocation. A polling snapshot fallback preserves usability when streaming is unavailable.

### Identity and privileged execution

Implement the authentication reference's user/credential split, tenant resolution, short-lived revocable sessions, scoped roles/permissions, explicit-deny precedence, owner ceilings for credentials, active/protected flags, cache invalidation, and audited administrative bypass rules. Resolve exactly one tenant before a domain operation. Conflicting tenant hints or conflicting authentication schemes fail closed. All data reads, searches, exports, events, backups, and worker commands enforce the same ownership rules.

Bootstrap local use with a protected single-use enrollment mechanism and a seeded local tenant/user; do not ship a universal admin password. The TUI exchanges protected local bootstrap material for a normal session. The browser uses a one-time pairing flow without credentials in URLs, then follows the same session model. Do not send password-equivalent headers over plaintext HTTP. Hosted login and credential exchange require TLS. Document supported authentication schemes, token encryption/signing, key rotation, constant-time comparisons, rate limits, revocation, and audit behavior following the authentication reference.

The contract must explicitly cover Bearer and equivalent `x-token` sessions, login-only `x-email`/`x-password` with tenant context, scoped access-key credentials, signed requests with bounded clock skew and nonce replay protection, and separately gated system-admin credentials as required by the reference. Contradictory carriers are rejected. If encrypted session tokens are used, implement the reference's AES-256 token model with a fresh IV/nonce per token, authenticated ciphertext, protected rotating keys and server-side revocation checks. Write a crypto/auth ADR covering the reference's SHA-256 password-equivalent representation, verifier storage and transport restrictions before implementing login; never treat an unsalted fast hash as a sufficient plaintext-password storage policy or quietly change the documented wire contract.

Endpoint/harness configuration and tool-enabled execution are privileged operations. Read-only users cannot trigger completions, launch processes, restore backups, or change configuration through hidden APIs. Remote multi-tenant process execution must run under an explicit OS isolation boundary, such as a dedicated account/container and workspace per tenant. Until that isolation is verified, hosted deployments disable shell-out execution by default and report the restriction; database tenancy alone is not process isolation.

Validate outbound endpoint URLs and protect cloud metadata/link-local targets. Local Ollama and deliberately configured private endpoints remain supported through an explicit administrator-managed policy. Keep same-origin deployment as the default, validate Host/Origin on sensitive local routes, configure a known CORS allowlist, and never trust forwarded headers without a declared proxy. Secrets must be redacted from request/response capture as well as logs.

## 10. TUIKit application and console commands

Armada's `AskThreadListView` is the reference for searchable conversations, unread/activity state, pinning, archiving, inline rename, paging, and row actions. Mux's `MuxTuiApp` supplies the streaming conversation/composer pattern and headless backend approach. Armor and S3Drive show application-shell, settings, startup, logging, and service-lifecycle patterns. Consult the actual TUIKit widget and hosting APIs at the pinned package version before implementing each screen.

The default terminal layout has a conversation list on the left, transcript in the center, optional run/participant details on the right, composer below, and persistent connection/status/key-help rows. At narrow widths, collapse secondary panes into drawers without losing the composer or active conversation. Scrolling up detaches from the live tail; new-message indicators let users return deliberately.

- [ ] Build startup/connect/pairing, first-run provider setup, conversation list, transcript/run detail, composer, endpoint/harness/participant forms, settings, health, request history, and permissions-aware administration screens.
- [ ] Make all core debate controls available with keyboard and mouse: start, continue, pause, resume, interrupt, stop, extend, follow-up target, note/directive mode, transcript export, and new conversation/debate.
- [ ] Preserve local drafts, selected thread, scroll position, recipient, and composer mode per client. Show revision conflicts when another surface changes the same shared draft.
- [ ] Provide localized help for every control, field, label, status, icon, and disabled state. Use TUIKit tooltips plus focus/status help and F1 so help does not depend on hover support.
- [ ] Add theme and language controls, command palette, searchable help, focus traversal, contextual key hints, and copy actions. Do not use ambiguous glyph-only actions without accessible help and a keyboard equivalent.
- [ ] Test paste and Unicode, multiline entry, Enter versus newline, CJK/emoji cell widths, RTL support/limitations, mouse capture, resize, modal focus, reconnect, and terminal restoration after failures.
- [ ] Keep global shortcuts from stealing printable input in text fields. Use TUIKit's focus/command routing rather than a custom competing input loop.

`ArgueBot.Cli` provides noninteractive commands using the same client and authorization. Proposed commands are `serve`, `tui`, `web`, `conversations list|show|create|rename|archive|export|delete`, `debate start|status|pause|resume|interrupt|stop|extend`, `message send`, `endpoints`, `harnesses`, `participants`, `settings`, `backup`, `restore`, and `doctor`. Final names and aliases are frozen in P0's CLI inventory. Add `--server`, `--home`, stdin/file input, cancellation, documented exit codes, and text/JSON/JSONL modes. Machine output goes to stdout; progress/errors go to stderr. Redirected input/output must not start a full-screen TUI.

Both scriptable commands and the TUI attach to an existing compatible service or start one through a documented local lifecycle. Exiting a client does not stop the service or its runs. An explicit service shutdown drains or interrupts work and persists checkpoints. Existing `go-terminal.sh/.bat` and `go-web.sh/.bat` remain as compatibility launchers during transition, with clear legacy/new behavior.

## 11. Web dashboard and route inventory

Move the frontend to `dashboard/` with the required React 19, Vite 6, Router 7 baseline and centralized fetch-based `ApiClient`. Verify and pin exact compatible dependency patches during implementation; the reference's version families are not an instruction to upgrade unrelated dependencies blindly. Use auth/app contexts, shared query/filter builders, normalized errors, and the same event protocol as the C# client. Do not retain page-specific raw fetch wrappers.

The first group in navigation is conversation work. Configuration, operations, diagnostics, and administration follow in separate groups. Keep a persistent shell with route title, server/tenant/principal/role, connection/health status, repository link, theme/language, and logout. The conversation list is searchable and independently scrollable, with recent activity, unread state, pin/archive controls, and background-run indicators.

| Route | User job and roles | Backend resources and filters | Actions and states |
| --- | --- | --- | --- |
| `/login`, `/setup` | Pair/sign in; first-time user/admin | Auth, bootstrap, locale, effective configuration | Language before login, pairing errors/expiry, endpoint setup and validation |
| `/dashboard/home` | Understand activity; all authenticated users | Counts, health, recent runs/issues, time-bucket activity | New conversation, resume, configure endpoint; empty/degraded states and external-tools card |
| `/dashboard/conversations` | Find/manage threads; permitted users | Conversation search, pin/archive, updated time, owner, paging/sorting | Create, rename, pin, archive/unarchive, JSON view, export, delete; no matches/error states |
| `/dashboard/conversations/:id` | Debate and follow up; owner/operator, viewer read-only | Message pages, runs, participants, events, drafts/read state | Composer, explicit targets, controls, new run, download; interrupted/reconnecting/forbidden states |
| `/dashboard/runs`, `/:id` | Inspect/control background work; operator/viewer by permission | State, conversation, participant, time range, paging | Inspect attempts/timing, pause/resume/cancel, context inspection; failures and stale revisions |
| `/dashboard/providers`, `/endpoints`, `/harnesses`, `/participants` | Configure reusable runners; permitted configuration operators | Capabilities, enabled state, provider kind, search/paging | Designed create/edit/view forms, test/validate, models, disable/delete, secret set/unset |
| `/dashboard/request-history` | Diagnose traffic; authorized operators | Time/method/path/status/principal filters, summary buckets, paging | Request inspector with redacted headers/bodies, JSON, copy, authorized clear/delete |
| `/dashboard/api-explorer` | Execute/inspect API; permitted developers/operators | `/openapi.json`, current auth/tenant context | Endpoint search, typed inputs, request/response panes, copy, custom destructive confirmation |
| `/dashboard/health`, `/logs` | Diagnose runner/service failures; authorized operators | Health, queue/runner state, redacted logs and recent failures | Refresh, filter, drill-down, live mode; unavailable endpoint/log states |
| `/dashboard/settings` | Edit service settings; admin save/restart, permitted read-only inspection | JSON settings API, effective overrides, version/server info | Grouped forms, validation, live/restart indicators, restart/reconnect recovery |
| `/dashboard/backups` | Protect/recover data; admin | Backup manifests/status, verify/restore | Create/download/verify/delete/restore with scope and recovery confirmation |
| `/dashboard/administration/*` | Manage tenants/users/credentials/RBAC/audit; scoped administrators | Typed identity resources and effective permissions | Applicable lifecycle forms, credential show-once/rotation, protected-role rules, denial inspection |

Primary resource pages need create/view/edit/delete where supported and authorized, secondary JSON inspection, row actions, copy controls, and custom modals. Shared tables have above-table count/range, page size, first/previous/jump/next/last, refresh, server filtering, sortable headers, and loading/empty/error/populated states. Menus render without clipping and do not trigger row navigation when a nested control is clicked.

Keep theme, transcript download, and new-debate utilities icon-only with localized accessible names and hover/focus help. Every control, label, text field, dropdown, status, and button gets useful help, including dynamically created controls and explanations for disabled actions. Preserve visible focus, semantic labels, focus-trapping modals, reduced-motion behavior, and status cues beyond color. Do not use browser `alert`, `prompt`, or `confirm` for workflows.

Use CSS design tokens, restrained framing, clear scroll ownership, and responsive panes rather than nested card stacks. Charts use small theme-aware SVG components, not a new charting dependency. Capture and inspect desktop 1280px, tablet 768px, and mobile 390px views. Verify dark/light at desktop and narrow widths, plus loading/error/empty/populated states, long labels, action menus, tables, and modal overflow. Missing visual evidence remains an open delivery gate.

## 12. Localization and shared user experience

Canonical translation catalogs and a locale registry live in `assets/i18n/`; build steps generate or package resources for the React and C# runtimes without creating separate translation sources. Select a proven .NET localization/message-format runtime after verifying compatibility with the catalogs and ICU-style interpolation/pluralization. Web uses i18next/react-i18next with ICU support.

The source/fallback locale is English. Plan the reference baseline languages: English, Spanish, Portuguese, French, Italian, German, Chinese, Arabic, Russian, Malay, Hindi, and Japanese, with documented regional/script variants. Ship expansion and RTL pseudo-locales. Locale selection resolves explicit selection, persisted client preference, profile preference, browser/system preference, then fallback; terminal system preference substitutes for browser preference.

- [ ] Localize pre-authentication and authenticated screens, console help, tooltips, placeholders, aria/alt text, key hints, page titles, validation, notifications, status labels, and server-authored display messages.
- [ ] Persist semantic codes and arguments, not rendered notification strings. Keep model/user message content unchanged and machine enum/API/database values stable.
- [ ] Apply `lang` and `dir` centrally before meaningful web paint. Add `Accept-Language` where applicable and shared locale-explicit date/time, number, duration, byte, relative-time, list, and plural formatters.
- [ ] Provide language selectors before login and inside both interfaces. Theme and locale survive reload, reconnect, and logout/login as documented.
- [ ] Check missing/orphaned keys, hard-coded UI strings, UTF-8 integrity, 30-50 percent text expansion, CJK wrapping, and RTL on every push/PR. Include English, a Romance language, CJK, and RTL in smoke tests.
- [ ] Add glossary, translator notes, key conventions, intentional untranslated terms, and a contributor guide. Record any terminal bidi limitations with tested behavior rather than claiming support from Unicode decoding alone.

## 13. Operations, telemetry, and deployment

Instrument the first implementation slice rather than adding diagnostics after the rewrite. Watson's built-in `Watson` meter/activity source covers HTTP; an `ArgueBot` meter/activity source covers commands, queueing, state transitions, storage, context preparation, generation, process launch/termination, replay, recovery, and backups. Subscribe to PolyPrompt and TUIKit sources where the process owns their exporters. One composition-root telemetry host per process prevents duplicate export.

Metrics include active/queued runs, turn latency and TTFT where measurable, runner errors/timeouts, cancellation latency, message/event persistence, SQLite contention, replay gaps, auth denials, and backup outcomes. Use bounded dimensions such as runner kind, operation, and outcome; never conversation IDs, prompts, endpoint URLs, or secret values as metric labels. Correlate logs and traces, mark errors explicitly, and avoid capturing conversation content by default.

- [ ] Add `TELEMETRY.md`, configurable retention/rotation, redacted crash reports, and a safe diagnostics bundle with preview and content policy.
- [ ] Provision Prometheus, Tempo, Grafana, and Loki for background execution logs in `docker/compose.yaml`, with pinned images, read-only configuration, stable datasource UIDs, and product-folder dashboards split into Overview, HTTP, Debates, Runners, Storage, and Security.
- [ ] Include required cURL healthchecks in HTTP images and the required `depends_on` conditions. Confirm required diagnostic images contain cURL or supply a compliant derived image.
- [ ] Avoid the default ArgueBot 4317 port colliding with OTLP 4317/4318 mappings. Keep internal service DNS addresses separate from browser-reachable published URLs.
- [ ] Mount service settings and the persistent home, use `restart: unless-stopped`, and implement graceful shutdown/restart. For native installs, use an equivalent supervisor/launcher so Settings restart actually returns the service to health.
- [ ] Provide plain and isolated factory/demo stacks using identical observability configuration. Demo data is fictional and never seeded into the user's real home implicitly.
- [ ] Supply `docker/update.bat` performing pull, down, up detached, then status without volume deletion; provide a matching shell helper. Keep destructive factory reset separate, explicit, and scoped to its own deployment.
- [ ] Link configured Grafana/Prometheus/Tempo/log tools from Home with browser-reachable URLs and copy controls. Show only documented development-default credentials in local demo mode, never real deployment secrets.
- [ ] Restrict metrics and observability listeners to trusted networks, change development credentials for hosted use, and document proxy trust, TLS, database permissions, and OS isolation for CLI runners.

Docker supports REST runners without assuming host CLI authentication or host tool access exists in a container. CLI harnesses require explicit installation, workspace mounts, identity, and permissions inside the execution environment. Document native and container capabilities separately; never mount a user's entire home or a Docker socket merely to make a demo work.

## 14. Verification and evidence

Tests prove behavior, benchmarks measure it, and simulated users find workflow defects. Each is a separate deliverable. The default automated path must run offline using deterministic runner fixtures and HTTP simulations, without spending provider quota or touching the user's `~/.arguebot`.

### Automated test inventory

Write reusable Touchstone descriptors in `Test.Shared`, referenced through `Test.Automated`, `Test.Xunit`, and `Test.Nunit`. Shared code references Touchstone.Core and the product under test, not test-framework assertion libraries, and writes no console output. Tests provision isolated homes/databases/ports, use `127.0.0.1`, clean up in `finally`, and report skips with reasons instead of hiding unavailable coverage.

| Suite | Required cases |
| --- | --- |
| Domain and prompts | Transition table, participant order, notes/directives, hold expiry across clients, consensus declaration, follow-up/new-run context, extension and conflicting commands |
| Persistence | First boot twice, CRUD/search/index behavior, SQLite/PostgreSQL parity, tenant/user isolation, constraints, migrations repeatedly and after interruption, future-schema refusal, history after restart |
| Recovery | Service crash during stream, finalized-turn durability, orphan process cleanup, stale leases, expired holds, missing credentials after restore, backup checksum/schema rejection, disk-full/permission/lock failures |
| Harness adapters | Fragmented UTF-8/JSONL, stderr pressure, final-snapshot deduplication, ANSI/CRLF, exit errors, bounds, timeout, interrupt/process tree, paths/quotes/Unicode, Windows shim behavior |
| REST adapters | Real adapter calls against simulated HTTP dependencies, auth failure, invalid model, 429/backoff, timeout, malformed/broken stream, usage/error mapping, cancellation and no duplicate retry |
| API and access | Every route/status/example, field validation, pagination/filter/sort, idempotency/revision conflict, secret masking, protected/global records, explicit deny, credential ceiling/revocation, all tenant boundaries |
| Event transport | Replay/live boundary, duplicate/dropped events, chunk offsets, cursor retention gap, foreign-tenant cursor, session expiry/revocation, slow consumers, fetch SSE parser and polling fallback |
| Cross-surface | Create in TUI/read in web, submit in web/view in TUI, simultaneous sends/control conflicts, independent holds, switching/closing without stopping runs, restart and reattach |
| TUI/console | Headless backend interaction, key focus, multiline/paste, resize, themes/locales, terminal restoration, non-TTY output, JSON/JSONL schemas, exit codes, launch/attach races |
| Dashboard | Route/component tests, permissions, all control help, theme persistence, icon-only actions, accessibility/focus, destructive modals, visual evidence at required widths and states |
| Operations/release | Health/readiness, telemetry export/redaction, settings live/restart behavior, Compose startup ordering, clean install/upgrade/uninstall preserving home, checksums and channel preflight |

Use Postman's API testing and simulation workflows for HTTP contracts and real runner code exercised against upstream stand-ins. Keep simulation definitions and runnable requests in the repository. CLI fixtures complement those simulations because stdio protocols are not HTTP. Opt-in live CLI/provider smoke tests use disposable directories, explicit timeouts and spend caps, and report live evidence separately from simulated evidence.

### Benchmark plan

Create root `BENCHMARKING.md` before round 0, an independent `src/Test.Benchmark` executable named `ArgueBot.Benchmark`, and the prescribed `benchmarks/` layout. The harness has no references to product assemblies and talks through public APIs/process entry points. Resolve paths from repository root, not invocation directory. Measure conversation history ingest/search, stream integrity, command correctness, replay/recovery, startup/list responsiveness, TTFT overhead, turn throughput, and cancellation latency. Separate orchestration overhead from model latency.

Implement `validate`, `ingest`, `quality`, `e2e`, `load`, `compare`, `history`, `stub`, and recommended `resilience`; add `prepare` if public datasets apply. Use fixed exit codes 0 success, 1 error/regression, 2 usage, 3 incomplete, and 130 cancellation. Commit a sanitized real conversation/protocol trace dataset with provenance and permission, plus a seeded synthetic dataset; both include negative probes. If no suitable approved real trace exists, mark that requirement blocked rather than fabricating one. Assess public debate/task datasets and document why each fits or does not; do not apply unrelated retrieval metrics to this product.

Record hypotheses before round 0. A starting hypothesis is that one shared engine eliminates cross-surface ordering divergence without adding unacceptable streaming overhead. Another is that bounded SQLite batch commits preserve responsiveness while meeting the documented crash-loss window. Measure both and revise using evidence. Never infer answer quality or truth from consensus rate alone. Any model judge needs human-label calibration and an explicit limitation statement; model-driven quality is not a deterministic CI gate.

Reports are paired JSON/Markdown with commit and dirty/untracked state, harness commit, dataset hashes, dependency versions, image digests, hardware, effective nonsecret settings, timestamps, sample counts, confidence intervals, and failures. Load includes warmup, open-loop support, and draining. Failures count against correctness and do not inflate successful throughput. Compare matches probes, fails mismatched sets unless explicitly allowed, uses tolerance plus paired significance for quality, and checks latency only on comparable machines. Save comparisons/history, maintain `benchmarks/RESULTS.md`, and never edit discarded reports into apparent successes.

Provide matching Bash 3.2-compatible `.sh` and `.bat` helpers for start-bench-server, run-baseline, and ci-gate. The gate excludes models, judges, and wall-clock latency; it compares deterministic correctness to reviewed committed baselines. Paid runs are opt-in with a hard budget. Keep raw reports/data/scratch ignored, scrub committed summaries, and record benchmark-discovered fixes in `CHANGELOG.md` and the evidence backlog below.

### Continuous integration

Run build, analyzers, automated/xUnit/NUnit suites, dashboard tests/build, localization checks, API artifact checks, and deterministic benchmark gate on every push to every branch and every pull request. Do not hide exclusions in branch/path filters. Use Windows, macOS, and Linux jobs for platform-sensitive product code; database-container and simulated dependency stacks can run in Linux jobs with that limitation documented. Upload logs, structured test results, screenshots, and benchmark reports using `if: always()`.

Define stable required status names for main-branch protection. Configure repository settings only when authorized; until then document the exact outstanding settings and do not claim enforcement. Build and smoke-test self-contained artifacts and separately evaluate AOT compatibility for the actual dependency graph. If Native AOT is advertised, add publish-and-run validation on all supported OSes; upstream package annotations alone are not proof that ArgueBot is AOT ready.

### Planned verification entry points

The following commands are acceptance interfaces to implement and document, not commands available in the current TypeScript repository. Add paired shell/batch wrappers where orchestration is needed. A clean contributor checkout should be able to run the same commands as CI without private paths or credentials.

```text
dotnet restore src/ArgueBot.sln --locked-mode
dotnet build src/ArgueBot.sln -c Release --no-restore
dotnet run --project src/Test.Automated -c Release -f net10.0 -- --results .run/results/automated.json
dotnet test src/Test.Xunit -c Release -f net10.0 --logger trx
dotnet test src/Test.Nunit -c Release -f net10.0 --logger trx
npm --prefix dashboard ci
npm --prefix dashboard run lint
npm --prefix dashboard run test:ci
npm --prefix dashboard run i18n:check
npm --prefix dashboard run build
npm --prefix dashboard run test:e2e
docker compose -f docker/compose.yaml config
docker compose -p arguebot-acceptance -f docker/compose.yaml up -d --wait
```

The test workflows repeat reusable-library coverage under each supported target framework and add provider-matrix and OS-specific jobs. API checks run the checked-in collection using safe environment variables through a pinned supported Postman runner; document its exact command after P0 verifies the tool version. `benchmarks/ci-gate.sh` and its batch counterpart own the deterministic benchmark stack/run/compare lifecycle. Supply scripts that provision an isolated home and credentials for browser/API tests; the command list must not assume the developer's normal service is already running. Acceptance teardown removes only that named deployment and its disposable data.

## 15. Documentation, migration, and repository cleanup

README is the primary installation and product guide, not a list of planned capabilities. During migration, label which commands run the legacy implementation and which use the new service. At cutover, show actual prerequisites, capabilities, storage ownership, and tested paths. Source builds need .NET/Node tooling as documented; self-contained installers must not imply users need those runtimes.

| Artifact | Required content |
| --- | --- |
| `README.md` | Product purpose, honest consensus limits, web/TUI/console quickstarts, CLI and REST runner setup, home layout, supported platforms, auth/pairing, screenshots, troubleshooting and links |
| `CHANGELOG.md` | `Unreleased` changes grouped by behavior, compatibility/migration notes, benchmark-discovered fixes; no invented release dates or version bumps |
| `REST_API.md`, OpenAPI, Postman | Full synchronized route surface, methods/parameters/bodies/status/auth/permissions/examples, stream/replay contract, safe variables and descriptions at collection/folder/request levels |
| `DOCKERHUB_README.md` | Product-specific getting started, architecture, persistence, container runner limitations, explicit image URLs, health and upgrade notes |
| `docs/ARCHITECTURE.md` | Projects/dependencies, domain and state diagrams, ownership, database providers, runner contracts and event durability |
| `docs/CONFIGURATION.md`, `docs/PROVIDERS.md` | File/database authority, override precedence, live/restart behavior, endpoint/harness forms, protocol/capability matrix, credential references and troubleshooting |
| `docs/STORAGE.md`, `docs/MIGRATION.md` | Schema/provider choices, upgrades, backups/restores, retention/delete rules, compatibility map, import/export formats and rollback |
| `docs/SECURITY.md` | Identity/RBAC, keys and sessions, secrets/redaction, local pairing, remote isolation, outbound URL policy and disclosure procedure |
| `TELEMETRY.md`, operations guide | Meters/spans, dashboards, Compose ports, logs, diagnostic capture policy, restart/update/factory reset and recovery |
| `docs/CLI.md`, `docs/TUI.md`, dashboard guide | Every command/flag, exit codes and machine output, keyboard/mouse help, fields/tooltips, threading workflows and permission differences |
| `docs/TESTING.md`, `BENCHMARKING.md`, `benchmarks/README.md` | Exact runnable commands, offline/live distinction, platform limitations, results/provenance and baseline-update policy |
| `docs/I18N.md`, glossary/catalog notes | Locale policy, extraction/review workflow, formatting, pseudo-locales, adding a language and terminal limitations |
| `CONTRIBUTING.md`, `AGENTS.md`, `CLAUDE.md` | Build/test/style guidance, requirements traceability, safe fixtures, version restrictions, documented package/reference boundaries |
| `LICENSE.md`, packaging license content | Preserve MIT and notices; include a matching `LICENSE` file where package metadata requires that filename |
| `.gitignore`, `.dockerignore`, build settings | Exclude secrets, local homes/databases/WAL, captures, build/install outputs and scratch; preserve safe examples and required test fixtures |

### Migration behavior

Existing running web sessions are memory-only and cannot be recovered from a stopped server. Offer export guidance before cutover and a validated, idempotent JSON importer for any supported legacy snapshot/export. Markdown imports are best-effort historical content with source provenance; never infer resumable internal checkpoints from a prose transcript. Unknown or ambiguous formats are rejected with an explanation.

Map existing CLI defaults/flags and browser localStorage settings into explicit new settings or participant/run defaults. A browser migration can offer a one-time reviewed import through an authenticated API; it must not silently upload local data to a remote server. Preserve the old theme preference. Existing launchers remain wrappers during migration, and old `/api/sessions` routes either have a tested time-bounded adapter or a documented breaking-change response. State the chosen compatibility period in P0 before clients are rewritten.

At cutover, stop legacy writers, export remaining reachable sessions, back up data/settings, start the new service, migrate/import, and verify counts/content and representative workflows. Rollback uses the saved old artifacts and pre-upgrade backup. Do not run older binaries against a newer schema or promise automatic downgrade. Leave user homes intact on uninstall unless the user explicitly requests data removal.

After parity acceptance, remove the obsolete TypeScript orchestrators, server and terminal input code, move `web/` into the supported dashboard structure, and remove unused Node backend dependencies and commands. Keep Node only for dashboard development/build where needed. Recheck scripts, image paths, license links, `.env.example`, package manifests, TypeScript configuration, README commands, archive references, CI paths, and distribution metadata. Git history preserves the prior implementation; a second unmaintained engine is not a compatibility strategy.

## 16. Actionable work packages

The dependency chain deliberately delivers storage, identity, and the execution contract before broad UI work. Each package is sized as a reviewable workstream with smaller commits inside it. UI prototypes may use fixtures earlier, but they cannot freeze a competing state model or bypass service authorization.

| ID | Priority / primary owner area | Depends on | Exit evidence |
| --- | --- | --- | --- |
| P0 | Required / architecture and repository | None | Baseline inventory, ADRs, requirements manifest, build/test skeleton and compatibility decision |
| P1 | Required / persistence | P0 | Home lifecycle, all database drivers/migrations, domain schema, backup/recovery tests |
| P2 | Required / security | P0, P1 | Local pairing and hosted identity/RBAC, session revocation, isolation matrix |
| P3 | Required / engine and runners | P0, P1, P2 context contracts | CLI parity, PolyPrompt adapters, durable engine and deterministic failure/restart tests |
| P4 | Required / service and clients | P1, P2, P3 command/event contracts | Versioned API, typed client, replay, OpenAPI/Postman/docs parity |
| P5 | Required / terminal and scripting | P4; locale foundation can start after P0 | TUIKit/console feature inventory complete and cross-surface tests |
| P6 | Required / dashboard | P4; shell/localization foundation after P0 | Full route inventory implemented, responsive/accessibility/theme evidence |
| P7 | Required / operations | Begins P0, completes after P4 | Instrumented service, provisioned stack, health/restart/settings verification |
| P8 | Required / verification and benchmarks | Begins P0; gates P1-P7 | Offline suites/CI, deterministic benchmark baseline and honest reports |
| P9 | Required / documentation and cutover | Incremental; final after P5-P8 | Reviewed docs, migration rehearsal, legacy removal and clean repository |
| P10 | Required / packaging and release readiness | Scaffold after P0; finalize P9 | Publisher matrix, verified artifacts/install paths, pending external gates listed |
| P11 | Required / product acceptance | P5-P10 candidate artifacts | Clean-instance simulated-user report, triaged findings, final acceptance matrix |

### P0. Freeze the intended behavior and bootstrap the replacement

The first implementation commits should make the plan testable without changing what users run by default. Characterization evidence is more useful here than a broad mechanical translation of the TypeScript code.

- [ ] Inventory every current flag, command, route, event, prompt, status, export, and setting. Add a parity checklist and fixtures for two/three participants and intervention/follow-up behavior.
- [ ] Capture requirements filenames/hashes and applicability in `docs/REQUIREMENTS.md`; record the decisions in section 3 as ADRs, including local authentication, database providers, and frontend migration.
- [ ] Verify pinned NuGet/package APIs from source and package restore. Establish TUIKit, PolyPrompt, Watson, PrettyId, database, Touchstone, localization, logging, and telemetry dependency choices with license checks.
- [ ] Create the solution/project layout, build configuration, analyzer/style rules, central dependency controls, and empty-but-runnable test adapters. Preserve the current product version unchanged.
- [ ] Freeze the CLI compatibility inventory, old-route transition policy, message/export schema versioning policy, and endpoint/run capability contract.
- [ ] Define locale/catalog conventions, user roles, route inventory, limits, and the run transition table. Inventory relevant Armada/Mux/Armor/S3Drive and frontend/backend reference files before copying patterns.

Gate: existing commands still work, the new skeleton builds without warnings, baseline fixtures run, and every requirement has a planned evidence source. A stubbed production feature does not count as parity.

### P1. Deliver durable storage and home management

Implement storage before allowing new runs so the engine never acquires an accidental in-memory-only execution path. Test the default local home through injected temporary paths, not by changing the developer's real files.

- [ ] Implement path resolution, settings authority/precedence, exclusive startup lock, schema discovery and permissions.
- [ ] Build domain-specific database interfaces, factory and drivers for both supported providers (SQLite and PostgreSQL), migrations, seeding and ownership constraints.
- [ ] Implement conversation/configuration/message/event/receipt CRUD and backend search/paging/sorting with indexes.
- [ ] Implement consistent backups, verification, controlled restore, retention and deletion behavior.
- [ ] Test repeated startup/migrations, crash recovery, lock contention, future schemas, invalid homes, permissions, disk failure, and backup round trips across supported provider workflows.

Gate: data survives process restart; tenant isolation and migration tests pass for every driver; no whole-domain JSON blobs; failed storage cannot report accepted work as durable.

### P2. Establish identity and execution permissions

Authentication precedes public mutation routes. Seed only the required protected identities and roles, and make first-use pairing straightforward without turning loopback into an authentication bypass.

- [ ] Implement principal/context resolution, local enrollment/pairing, hosted login/exchange, revocable sessions, credential lifecycle and required scheme validation.
- [ ] Add role/permission mapping for conversations, run control, generation, configuration, filesystem-capable harnesses, settings/restart, backups and administration.
- [ ] Implement scoped assignments, explicit-deny rules, credential owner ceiling, active/protected checks, effective permissions, cache invalidation and denial audit.
- [ ] Implement secret references/encryption boundary, capture redaction, TLS/Origin/CORS policy and hosted runner isolation defaults.
- [ ] Test direct URL/API access by viewers, tenant conflicts, revoked credentials mid-stream, foreign resource IDs, protected roles, URL policy, and privileged actions.

Gate: restricted users cannot operate hidden endpoints, and every transport/worker uses the same permission decision. Hosted CLI execution remains disabled until OS isolation has its own evidence.

### P3. Port the engine and runner adapters

Port the existing useful behavior into the new state model rather than preserving the two old loops. Keep provider-specific details inside adapters and prove streamed behavior with deterministic fixtures.

- [ ] Implement CLI adapters and the generic stdio protocol, validated configuration forms/contracts, diagnostic actions and fixture process.
- [ ] Implement PolyPrompt completion adapters and supported model discovery/validation, capability-driven options, cancellation, limits and retry rules.
- [ ] Implement the command transition table, idempotent command processing, checkpoints, run leases, message/event batching, recovery and context selection.
- [ ] Implement cross-client holds, note/directive visibility, follow-ups, subsequent runs and consensus reporting with preserved provenance.
- [ ] Run characterization, process-tree, simulated REST fault, state-conflict and restart suites. Record actual differences from the TypeScript implementation.

Gate: one engine produces equivalent documented behavior for both surfaces; completed messages survive restart; uncertain operations are not rerun automatically; no live provider is needed for normal CI.

### P4. Expose and verify the service contract

The shared clients become the only way user interfaces mutate service-owned state. API design includes failures and recovery rather than only happy-path DTOs.

- [ ] Implement the Watson host/composition and route registrars, typed request/context/error/paging contracts, limits and versioned routes from section 9.
- [ ] Implement authenticated replayable SSE, session revalidation, heartbeat, snapshot/poll fallback, and typed C# transport client.
- [ ] Implement settings read/write/restart metadata and request capture/history with truncation, redaction, retention and zero-filled activity buckets.
- [ ] Keep OpenAPI, `REST_API.md`, Postman requests/examples/assertions and any compatibility adapters synchronized; run contract checks.
- [ ] Verify two clients attached to the same conversation, duplicate command receipts, disconnect/reconnect, foreign cursors and retention gaps.

Gate: all route families have working authorized lifecycle operations and documentation; model execution survives client disconnects; replay tests prove no duplicate displayed text or lost finalized turns.

### P5. Replace terminal surfaces with TUIKit and console commands

Build a real application shell around the shared client. The TUI must not contain a fallback orchestration loop or perform direct database writes.

- [ ] Implement service startup/attach/compatibility diagnostics, TUIKit shell and thread list, transcript, composer and run controls.
- [ ] Add runner/participant setup and editing, settings, backups, diagnostics, authorization-aware administration, theme, locale and contextual help.
- [ ] Implement all scriptable commands, non-TTY behavior, stdout/stderr rules, exit codes and machine-output schemas.
- [ ] Add headless UI tests, key/focus/resize/paste/Unicode cases, recovery paths, and cross-surface workflows.
- [ ] Update paired terminal/web launchers and user help while retaining the documented migration option.

Gate: every documented terminal task is tested through the UI/commands, help is available without mouse hover, and exiting/reopening the TUI preserves history and background runs.

### P6. Ship the complete dashboard

Shared components and route contracts come before one-off page implementations. Deliver the whole operator experience described in section 11 and make missing states explicit while integrating.

- [ ] Create React/Vite structure, auth/app contexts, shared API/event clients, shared forms/tables/modals/help/copy/status components and i18n bootstrap.
- [ ] Implement every route in the inventory with authorized lifecycle actions and designed empty/loading/error/populated states.
- [ ] Add conversation navigation, background activity/unread state, drafts, explicit recipient selection, run controls, exports and context inspection.
- [ ] Implement configuration, request history/inspector, OpenAPI Explorer, settings/restart, health/logs, backups and applicable administration.
- [ ] Retain icon-only theme/download/new-debate controls and add hover/focus help to all static/dynamic controls and labels.
- [ ] Capture required responsive/theme/localization screenshots, inspect them, fix clipping/focus/overflow defects, and retain evidence.

Gate: the route inventory has no unexplained omissions; no ad hoc page REST clients; all primary workflows and required visual/accessibility states pass review.

### P7. Complete operability as features land

Logs and metrics need to explain a slow or failing debate without source inspection. Dashboard diagnostics and deployment configuration are part of the feature being built.

- [ ] Wire built-in and application telemetry from the first server slice, with redaction and low-cardinality tests.
- [ ] Commit and verify the Compose stack, provisioning, health/startup dependencies, mounted persistence/settings and native service supervision.
- [ ] Add Grafana domain dashboards, request-history retention, log/crash rotation, endpoint health, diagnostics bundles and external-service links.
- [ ] Test live settings, restart-required changes, restart recovery, update without data loss, dependency outages and scoped factory reset.

Gate: a clean deployment produces metrics/traces/logs and useful dashboards in one documented startup; an operator can identify a failed runner and recover a paused run.

### P8. Close automated and measured verification

Every earlier package contributes tests here; P8 verifies the complete matrix and establishes trustworthy baselines rather than postponing testing to the end.

- [ ] Complete the suite inventory in section 14 through all Touchstone adapters, browser/component tests and real transport/process fixtures.
- [ ] Wire every-push/every-PR CI, three-OS coverage, database-container jobs, safe API simulations and always-uploaded evidence.
- [ ] Deliver benchmark plan/harness/datasets/stack, round 0 reports, comparison/history and reviewed deterministic baseline gates.
- [ ] Run opt-in live tests only when explicitly configured, clearly separating paid/live evidence from deterministic gates and reporting all skipped coverage.
- [ ] Produce a release-candidate evidence summary with commit, environments, exact commands, pass/fail/skip counts and unresolved risks.

Gate: required checks pass; no missing provider or platform coverage is disguised as a pass; benchmark claims link to reproducible reports.

### P9. Finish documentation and rehearse cutover

Documentation follows the implementation throughout; this package verifies it from the viewpoint of someone with a fresh checkout and no prior knowledge.

- [ ] Complete every artifact in section 15, revise README claims, add accurate screenshots/examples, and update `CHANGELOG.md` under `Unreleased`.
- [ ] Rehearse legacy exports/imports, browser-preference migration, new-home creation, upgrade, backup/restore and rollback using disposable data.
- [ ] Follow every README/help/API/Postman example literally and record/fix documentation mismatches in the normal implementation workflow.
- [ ] Remove obsolete engines/dependencies only after parity gates, update all entry points/build paths/ignores, and scan for secrets, absolute developer paths, stale claims and em-dashes in new material.
- [ ] Reconcile the requirements matrix and this plan against actual files and evidence; list intentional future extensions separately.

Gate: the repository has one supported engine, all documented commands are executable, and migration/rollback behavior has evidence.

### P10. Prepare distribution and verify native installation

Packaging logic belongs in `ArgueBot.Publisher`, driven by root `publisher.json` and a native OS CI matrix. The manifest describes artifacts/channels/RIDs without embedding a changing release version. Paired root `build-installers.sh/.bat` scripts are thin wrappers used by local builds and CI.

- [ ] Produce self-contained single-file artifacts for win-x64, osx-x64, osx-arm64, linux-x64 and linux-arm64; win-arm64 is an explicitly tracked optional target. Include native SQLite extraction and dashboard assets correctly.
- [ ] Add Windows Inno/WiX installer, macOS app/terminal-launch packaging in DMG/PKG with free ad-hoc signing, and Linux DEB/RPM/AppImage with appropriate user-service integration. Do not require paid signing/notarization.
- [ ] Provide Winget, Chocolatey, Scoop, Homebrew, apt/yum and cross-platform dotnet-tool channels. Snap/Flatpak are implemented when enabled in the manifest. Keep manifests/assets in this repository; only a genuinely required Homebrew tap can use the stated satellite-repository exception.
- [ ] Generate SHA-256 checksums and package metadata/readme/icon/MIT license/symbol packages as required. Write installer outputs under ignored `installers/<approved-version>/`.
- [ ] Add honest unsigned-download notices and platform guidance. Sign apt/yum repository metadata with a free generated GPG key, distinct from code signing.
- [ ] Add release preflight/dry-run and native clean-install/upgrade/uninstall tests. Keep user data on uninstall by default. Verify service supervision, browser launch, terminal launch, paths and side-by-side compatibility.
- [ ] Identify exact maintainer-provided credentials/accounts/keys, repository settings and external moderation gates. Automate up to those boundaries and report pending submissions honestly.
- [ ] Publish only under a separately authorized release/version instruction after all release gates pass. Verify install commands after publication; a submitted manifest is not proof a channel is live.

Gate: verified artifact matrix and checksums exist; installer and package-manager smoke results distinguish built, submitted and publicly available states. No release is labeled fully distributed while a required channel remains unavailable.

### P11. Run full-product acceptance as a simulated user

Use clean isolated homes, volumes, ports and documented installation paths. Start with user-facing docs/help, inventory every surface, then exercise it as first-time evaluator, daily user, integrating developer, operator, restricted user and benign hostile-input user. Do not use source inspection to choose journeys or repair a failed step invisibly.

- [ ] Create a coverage matrix for every page, command/flag, endpoint, Postman request, settings flow, installer and diagnostic surface.
- [ ] Exercise complete resource lifecycles, non-ASCII/large/invalid input, permissions, duplicate submissions, simultaneous clients, restart, dependency failure, upgrade, backup/restore, and every documented example.
- [ ] Record environment/commit/dirtiness and startup time; attach scrubbed exact steps/input/expected/actual/evidence for each finding with severity and oracle.
- [ ] During the observation session, do not fix the product. Present ranked findings and obtain the user's decisions, as required by `SIMULATED_USER_TESTING.md`; record accepted/deferred/declined items.
- [ ] Address approved findings in separate changes with regression tests, rerun their journeys on a fresh instance, and update docs/changelog/evidence.
- [ ] Tear down isolated state or explicitly list what remains. Never reset the user's real `~/.arguebot` as test cleanup.

Gate: the user has the report and decisions, approved release-blocking findings are resolved and reverified, and every untested surface has an explicit reason and release consequence.

## 17. Product acceptance scenarios

The following scenarios are the minimum end-to-end release bar. Expand them into automated cases and simulated-user journeys; a screenshot of a thread list is not evidence that conversations are durable or shared.

1. From a clean install, pair the TUI and browser to the same service. Configure a CLI harness and a PolyPrompt endpoint, create participant profiles, and run a mixed three-participant debate.
2. Create a conversation in the TUI, view its streaming turns in the browser, send a browser moderator note, and confirm ordered identical history in both surfaces without duplicate text.
3. Switch conversations and close both clients while a debate runs. Reopen and observe the current state and all finalized history without starting another worker.
4. Pause, steer a specific next participant, interrupt, extend and stop from either surface. Verify authorized recipient context, independent typing holds, actionable conflict errors and consistent outcomes.
5. Finish a debate, ask a selected participant a follow-up, and start a second debate in the same conversation using an explicit context selection. Historical settings and earlier messages remain unchanged.
6. Kill the service during a streamed turn, restart it, and recover a paused/interrupted run. Retain finalized turns and persisted partial content, and never repeat uncertain external execution silently.
7. Edit/disable/delete endpoint and participant configurations while old history exists. Active work follows documented snapshot rules and historical attribution stays readable.
8. Search, rename, pin, archive/unarchive, export and delete a conversation. Verify paging/counts, authorization, active-run conflicts, event/read-state cleanup and search-index consistency.
9. As a viewer, fail to mutate state through the UI and direct API calls. As another tenant, fail to discover messages, streams, settings, credentials or exports. Revoke a session and verify active stream access ends.
10. Back up, verify and restore into an isolated home; confirm history/configuration/schema and missing-secret diagnostics. Test a corrupt bundle without modifying live data.
11. Use all terminal and web controls with help text, keyboard focus and a non-English locale. Verify light/dark, mobile/tablet/desktop, CJK/RTL layouts and terminal resize/paste behavior.
12. Diagnose a simulated timeout/rate limit/process failure using request history, logs, health and Grafana. Change a live setting, apply a restart-required setting, and watch both clients recover.
13. Install and upgrade native packages, launch both surfaces, use scriptable JSON/JSONL commands, and uninstall while preserving the home. Run all documented quickstarts and API examples.

## 18. Risks, sequencing, and explicit future work

The largest risk is scope expansion without a single working execution path. Build one durable vertical slice first: isolated home, authenticated service, one conversation, one fixture runner, persisted turn, two attached clients and restart recovery. Then add the other adapters and operational pages against that proven contract. Do not parallelize UI implementations around separate guessed engines.

| Risk | Mitigation and evidence |
| --- | --- |
| Losing existing harness behavior | Characterization fixtures plus opt-in CLI smoke tests; preserve tool/auth/cwd distinctions |
| SQLite contention and streaming overhead | Bounded batch commits, one writer owner, contention tests and comparable-machine benchmarks |
| Duplicate work after retries/crashes | Receipts, state revisions, durable attempts, run leases and explicit interrupted recovery |
| Security boundary confused with display filtering | Server-side authorization on every resource/stream/worker plus cross-tenant negative tests |
| Hosted process access crosses tenants | Verified OS/container workspace isolation or shell-out disabled in hosted mode |
| Secrets leak through diagnostics/library capture | Central capture/redaction policy, secret references, tests across request/history/log/export paths |
| Provider/API differences | Capability-driven forms and runner contracts; verify pinned package APIs and test actual adapters |
| Rewrite stalls distribution | Keep legacy runnable until parity; scaffold packaging/CI early; track external release blockers separately |
| Localization/visual quality becomes last-minute work | Shared catalogs/components and required screenshots from the first dashboard/TUI slice |
| Requirements change during implementation | Source-hash traceability, explicit ADR/plan updates, final applicability audit |

Conversation branching, attachments/RAG, automatic tool execution for REST participants, scheduled debates, remote worker pools, external identity federation, MCP, and public SDKs remain future capabilities. Keep extension boundaries and provenance sufficient to support them, but do not expose nonfunctional placeholders or claim they ship with this rewrite.

### Evidence-driven improvement backlog

Use this table for benchmark or acceptance findings as implementation proceeds. Estimated benefits are planning judgments, not measured results. Keep the original baseline/report link and add the outcome only after remeasurement.

| Candidate | Priority / value and simplicity | Initial evidence | Metric or acceptance target | Result |
| --- | --- | --- | --- | --- |
| Durable shared engine and thread history | Required; highest product value, substantial implementation | Separate loops and memory-only web sessions at `e4a2f73` | Cross-surface parity, finalized-turn durability, explicit recovery | Not implemented/measured |
| Configurable CLI and REST participants | Required; expands runner choice, moderate/high complexity | Fixed agent union and CLI-only execution | Mixed-runner acceptance and fault-handling matrix | Not implemented/measured |
| TUIKit and complete dashboard surfaces | Required; navigable persistent product, substantial scope | Current linear terminal and single-session web layout | Route/command coverage, help, responsive and headless tests | Not implemented/measured |
| Stream commit/replay tuning | Measure before optimizing; moderate complexity | New durability requirement introduces a tradeoff | Bounded crash-loss window, no duplicate replay, orchestration overhead | Await round 0 |

### Where implementation departs from this plan

October 19, 2026: the user explicitly narrowed database support to SQLite and PostgreSQL; see ADR 0003. Complete-service recovery metadata is a protected global resource because its archives span tenants; see ADR 0004. Controlled full-bundle restore now supports new SQLite homes and explicitly provisioned empty PostgreSQL destinations. Post-commit publication failures retain protected operator recovery state; this cross-resource recovery boundary is documented in ADR 0004.

Recovery evidence: 22 shared descriptors pass locally with PostgreSQL, CLI process fixtures and PolyPrompt transport enabled, with zero skips. Ten browser journeys pass, including encrypted archive creation, verification, authenticated download and deletion. Complete-home recovery tests exercise session revocation, key rotation, corruption, traversal, quota, wrong passphrases and transaction rollback. Schema migration 4 records artifact inventory and durable administrative jobs; encrypted bundles embed the locked dependency inventory. These slices do not complete P1, P4, P5, P6 or release acceptance.

Ownership evidence: 24 shared descriptors pass locally with both supported providers and runner fixtures enabled. Independent SQLite configurations cannot acquire the same file; PostgreSQL refuses a competing service, signals forced writer loss, allows a successor and fences the old owner from reconnecting. Per-run attempt/context/stream batching gates remain open.

Identity boundary evidence: migration 5 distinguishes explicit system/tenant administrator flags from role names. Session tests prove privilege changes and user/tenant disablement take effect without reissuing tokens; historical schema-4 snapshots restore and upgrade to the explicit flags. Full identities, credentials and scoped RBAC remain open P2 work.

Streaming slice: configurable size/timer commits and schema-6 UTF-16 event offsets are implemented. Tests reconstruct burst output exactly from committed fragments, persist a stalled runner’s partial text and exercise typing-hold expiry independently of wall-clock adjustments. This supplies correctness evidence; benchmark round 0, context/attempt provenance and full replay-retention gates remain open.

Terminal configuration slice: Ctrl+E opens service-backed participant lifecycle forms, including CLI/REST protocols, literal argument rows and bounded execution settings. Headless checks cover 60/100-column layouts in both themes, required fields, save/cancel and service-error draft retention. Twenty-seven local shared descriptors pass with all supported provider/runner fixtures enabled; full terminal operations and localization gates remain open.

Recovery navigation slice: Ctrl+K in the TUI provides archive creation/verification/download/deletion, isolated SQLite restore and durable job status. Web, console and terminal share a paginated operation inventory, including jobs from earlier client sessions. Headless recovery forms verify masked passphrases and refuse path traversal; captured frames exposed and corrected scrollbar clipping. Twenty-eight shared descriptors and ten browser journeys pass locally. Full P2 security and remaining operational/localization surfaces stay open.

Windows CI follow-up: the new command-line smoke timed out while capturing `service start` on Windows, while macOS/Linux and integration/browser jobs passed. Windows launch now explicitly restricts inherited handles using the documented native handle-list API. Hosted run 39299208420 passed all three runtime platforms, integration and dashboard, including the captured CLI smoke.

PostgreSQL recovery parity: schema 7 records explicit destination provider/reference and retained staging. Thirty-one shared descriptors pass locally with all fixtures enabled, including SQLite-to-PostgreSQL complete archives, nonempty-target refusal and retained state after injected publication failure. The CLI/HTTP smoke passes twelve assertions with an isolated PostgreSQL fixture. Native clients verify a fresh port-bound home proof before using cached CLI sessions; concurrent key creation is locked and tested. A second home cannot accidentally attach to a healthy listener for another home. Invalid credential-reference values are omitted from request history. These are verified slices; the milestone checkboxes remain open for their remaining complete gates.

Security accounting slice: schema 8 adds validated session/carrier, correlation and target metadata to append-only events. Session issuance/revocation commit with their events; HTTP denials are recorded even when optional request capture is disabled. SQLite/PostgreSQL checks enforce tenant/system administrator inspection and discard unvalidated principal hints. Native HTTP checks reject mixed schemes and ambiguous carriers. Web, CLI and Ctrl+J terminal inspection are available, with read-only headless paste/scroll checks. Thirty-four shared descriptors pass locally with zero skips. Full hosted identity, credentials, scoped RBAC and comprehensive administrative accounting remain open P2 gates.

Generation provenance slice: schema 9 atomically accepts pending attempts, answers and exact source-context identifiers, and atomically finalizes answers, attempt results, successful directive consumption and speaker checkpoints. Runs freeze their original question. Explicit interrupted retries preserve directives without exposing them to another participant or later completed turn. Character/message budgets pause before invocation without truncation. Web/CLI/TUI expose paged provenance inspection; Ctrl+G opens the terminal workflow. Thirty-nine shared descriptors pass with SQLite/PostgreSQL and real runner fixtures, and twelve browser journeys include an actual PolyPrompt fixture debate. A final-turn pause regression now completes an exhausted run without another invocation. Explicit user context selection, summarization/model token budgets, complete provider usage/exit metadata remain open P3/P4 work. No full milestone is marked complete.

Context preparation follow-up: the engine now bounds eligible metadata and total UTF-8 payload before materializing selected content, then enforces exact UTF-16 limits without truncation. SQLite/PostgreSQL checks cover chronology, CJK/emoji/Arabic, recipient exclusions, exact character/message limits and refusal to append to completed sources. The existing thirty-nine shared descriptors pass with all provider/runner fixtures. Public transcript pagination and explicit user-selected context remain separate open work.

Bounded history slice: attachment protocol 2 replaces the unbounded message-array route with typed preview pages, exact totals and reusable membership boundaries, plus an individual full-message route. Web/TUI polling now uses bounded history; Ctrl+L opens terminal pages and read-only full content. Web downloads and native streaming JSONL/Markdown hydrate complete messages. Tests cover more than one hundred HTTP messages, long Unicode previews/full content, cancellation, concurrent appends, tenant/conversation isolation and old-protocol credential refusal. Forty-one shared descriptors, fifteen CLI assertions and thirteen browser journeys pass locally with both providers and fixtures enabled. Replay retention, scoped identity administration, full localization and distribution gates remain open.

Incremental evidence: event retention now preserves a contiguous replay suffix and a monotonic latest anchor on SQLite and PostgreSQL, independently of permanent transcript history. Bounded replay pages, expired/future cursor errors, native SSE parsing and dashboard snapshot recovery have executable coverage. Oversized runner callbacks are split into durable batches; malformed trailing Unicode produces a failed attempt with a preserved valid prefix. These slices do not close the remaining P3/P4/P7 gates.

Principal-boundary evidence: direct conversation/profile reads and mutations reject unauthenticated contexts and missing tenant/user bindings before querying storage. Shared SQLite/PostgreSQL checks preserve authenticated read-only access and reject forged owner IDs. Full scoped RBAC remains an open P2 gate.

Tenant identity management now has dashboard, Ctrl+U terminal and console surfaces, password exchange, protected-owner checks, revision conflicts and immediate session revocation after password changes or disablement. See [identity behavior and remaining security gates](docs/IDENTITY.md).

Optional loopback HTTPS now loads an operator-provisioned `keys/listener.pfx` with a password environment reference. Password exchange requires HTTPS. Native clients bind self-signed local trust to the owner-protected home/endpoint certificate pin; browsers use operator-managed certificate trust. TLS certificate keys follow encrypted opt-in recovery rules. Hosted ingress and runner isolation remain open.

Identity/HTTPS slice verification: 48 shared console descriptors passed with SQLite, real PostgreSQL and real PolyPrompt fixture transport; 15 complete browser journeys passed over a disposable HTTPS listener; console smoke passed 22 assertions spanning default HTTP pairing, HTTP password refusal, HTTPS attachment, identity login/disablement and recovery. NUnit and xUnit passed on .NET 8 and .NET 10. Mobile and desktop identity forms have light/dark rendering evidence. These are incremental P2/P5/P6/P7/P8 results, not complete milestone closure.

Worker authority slice: SQLite/PostgreSQL tests distinguish session logout from current identity revocation. Active generation is interrupted after execution-role removal or account disablement, stale-context resume creates no new attempt, and `run.authorization` audit records preserve the accepted principal and run. Full scoped-role/credential authorization remains an open P2 gate.

Scoped authorization foundation: a bounded pure evaluator and shared truth-table descriptor cover explicit deny, aggregate Write semantics, exact resource IDs, tenant inheritance, ownership narrowing, administrator bypass and credential owner ceilings. This is not yet wired to stored role administration or the HTTP operation map. See [implementation boundaries](docs/AUTHORIZATION.md); P2 remains incomplete.

Password storage hardening: schema 11 derives salted PBKDF2-HMAC-SHA256 verifiers without changing the password-equivalent wire contract. Existing schema-10 accounts and sessions survive upgrade; replay of stored verifier material fails. Evidence: 51 shared console descriptors including PostgreSQL and PolyPrompt, NUnit/xUnit on both target frameworks, 15 HTTPS browser journeys, 22 console assertions and OpenAPI syntax lint. Historical pre-upgrade backups retain sensitive legacy equivalents. P2 remains open for stored roles, credentials and full enforcement.

Stored authorization foundation: schema 12 introduces role, permission, mapping and scoped-user-assignment tables. Initialization refreshes eight protected global built-ins without replacing existing role IDs. Current-user resolution reads consistent current database facts, rejects inactive fallback and bounds assignment/grant expansion. This does not yet expose management APIs or replace compatibility route authority. P2 remains open until those APIs, credential resolution, effective-permission interfaces and complete route/worker/list enforcement pass their gates. Local tests, rather than hosted CI completion, are the execution gate per user instruction.
