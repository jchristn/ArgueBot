# Resources plan

Status: implemented on `feature/v2.2.0` on November 5, 2026 (see [Execution record](#execution-record)); the image build and push wait for the merge. Decisions were confirmed by the product owner on November 5, 2026, and the numeric limits marked "proposal" were adopted as proposed. Target version: 2.2.0 (version change requested by the product owner on November 5, 2026), on a `feature/v2.2.0` branch from `main`. Prepared November 5, 2026.

This plan adds **Resources**: named markdown documents stored in a tenant's workspace. Agents can list, search, read, create and edit them through tools during a debate. A prompt or role instruction can then say "if you need to know more about billing, call `read_resource` with name `billing`", and the agent pulls that context in only when it needs it. Agents can also write their own resources, so they can record findings and build context that later turns and later debates reuse.

Each phase lists its tasks, the files involved and the evidence required before its box is checked. Do not check a box without that evidence. Every phase that changes behavior updates its code, tests, API artifacts (`assets/api-contract.json`, `REST_API.md`, `MCP_API.md`, the OpenAPI document and the Postman collections) and documentation together.

## Decisions

These decisions come from the product owner and override any conflicting default in the requirements.

| Topic | Decision |
| --- | --- |
| Storage | Resources are database rows, not files on disk. The service stays the sole writer, tenant isolation, SQLite and PostgreSQL support come for free, and no agent needs file access. Import and export of `.md` files cover the "it's a markdown file" workflow. |
| Scope | One library per tenant, shared by every user in the tenant, like prompts and agents. Each debate can narrow which resources its agents see (Phase 3). |
| Name | Each resource has a unique, case-insensitive `name` within the tenant: lowercase letters, digits, `-`, `_`, `.` and `/`, 1 to 128 characters (for example `billing`, `team/conventions`). This is what agents pass to `read_resource`. The `res_` PrettyId is the stable ID; names can be renamed. |
| Content | Markdown body up to 256 KiB, plus a `title` and a one-line `description` that `list_resources` shows so an agent can decide what to read. Optional `tags`. |
| Revisions | Every change creates a revision row with the full body, author (user or agent seat, run and attempt) and time. Updates require the current `revision` (optimistic concurrency), so two agents cannot overwrite each other silently. Users can view history, diff and revert. |
| Agent write access | A per-debate setting: `off`, `read` (default) or `read_write`. Each resource also has a `locked` flag. Agents can never change a locked resource, so curated reference material stays stable while agents keep notes in other resources. |
| Tool names | `list_resources`, `search_resources`, `read_resource`, `create_resource`, `update_resource`, `edit_resource`, `append_resource`. They are not offered when access is `off`, and the four write tools are offered only with `read_write`. |
| Transport | The tools join the existing conversation tool catalogue (`ConversationTools`), which already reaches every agent type: MCP for Claude Code and Codex, the JSON tool envelope for Mux, Gemini and generic CLIs, and native tool calling for REST models. They are plain tools, not the MCP "resources" primitive. CLI support for MCP resources varies and the envelope and REST paths have no such primitive. |
| Trust | Resource text is returned as data, like conversation evidence. Agent-written revisions are marked as agent-authored in tool results and in the UI, because an agent's note persists into later debates. |

The three questions left open in the first draft were answered on November 5, 2026:

1. Agent-created resources go straight into the tenant's shared library, marked as agent-authored, with revision history and revert.
2. New debates default to `read` resource access; `read_write` is turned on per debate.
3. Prompts get an opt-in `{resources}` placeholder that expands to the names and descriptions of the resources the debate can see.

## Requirements compliance

Before Phase 1, re-read `BACKEND_ARCHITECTURE.md`, `BACKEND_TEST_ARCHITECTURE.md`, `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `I18N.md` and `CODE_STYLE.md` in `~/Code/Agents/requirements`. Confirm their hashes against `docs/requirements-manifest.json`, and record any deviation in `docs/REQUIREMENTS.md`. Resources follow the existing rules: PrettyId strings, typed contracts, tenant-scoped domain methods, both database providers, audited changes, localized UI text, and code, tests, API artifacts and documentation updated together.

## Current state

- Conversation tools are defined in `src/ArgueBot.Core/Services/ConversationTools.cs`: `list_turns`, `get_turn`, `search_context`, `get_supporting_details`, `get_debate_state`, `get_summary` and `request_clarification`. They share one JSON schema and are bound to one invocation (run, participant, source boundary, attempt).
- `ConversationEngine` passes `ConversationTools.Definitions()` and `ExecuteAsync` to `RunnerRouter.GenerateWithContextAsync`. That method routes to `McpInvocationRunner` (Claude Code and Codex, through `McpBridge`), `ContextEnvelopeRunner` (other CLIs) or `PolyPromptRunner` (REST).
- Each tool call reserves budget through `Budgets.ReserveToolAsync` (per-turn limit of 32 calls and 16 continuation rounds).
- Prompts are stored per tenant with placeholders rendered by `PromptCatalog` (`src/ArgueBot.Core/Services/PromptCatalog.cs`).
- The schema is at version 32 (`DatabaseSchema.CurrentVersion`).

## Phase 0: Branch and move to v2.2.0

- [x] Create `feature/v2.2.0` from `main`.
- [x] Set the version to 2.2.0 everywhere 2.1.0 appears as the product version: `Directory.Build.props`, `dashboard/package.json`, `dashboard/package-lock.json`, `dashboard/src/App.jsx` (sidebar), `scripts/generate-api.mjs`, `scripts/generate-v2-workflow.mjs`, `src/ArgueBot.Core/Observability/ProductTelemetry.cs`, `src/ArgueBot.Tui/SplashScreen.cs`, `src/ArgueBot.Server/Routes/TokenRoutes.cs`, `src/Test.Shared/TerminalSuites.cs` (splash assertion), `src/Test.Shared/PackagingSuites.cs`, the project references in every `packages.lock.json` and `packaging/locks/*/*.json`, and `assets/dependencies.json`. Search the tree for `2.1.0` afterwards (outside CHANGELOG history and `archive/`) and justify every remaining hit.
- [x] Regenerate the API artifacts (`node scripts/generate-api.mjs` and the v2 workflow generator) so `assets/postman/openapi.json`, `assets/postman/arguebot.postman_collection.json`, `assets/postman/v2-acceptance.postman_collection.json` and `REST_API.md` carry 2.2.0.
- [x] Docker: `docker/compose.yaml` uses `image: jchristn77/arguebot:v2.2.0`; update `docker/README.md`, `DOCKERHUB_README.md` and the examples in `build-all.sh` and `build-all.bat` to `v2.2.0`.
- [x] README: replace the "v2.1.0 (in development on `feature/v2.1.0`)" note with a v2.2.0 note that introduces resources and links to `docs/RESOURCES.md`, and keep a short line on what 2.1.0 added.
- [x] CHANGELOG: start `## Unreleased: v2.2.0` above the 2.1.0 section, with an introduction pointing to this plan. Leave the 2.1.0 entries as the released history.
- [x] `docs/REQUIREMENTS.md`: add a "v2.2 resources scope" section recording the decisions above and any deviations.

Acceptance: `dotnet build` with no warnings; the Terminal and packaging suites pass with the new version; `docker compose -f docker/compose.yaml config` shows `jchristn77/arguebot:v2.2.0`; the regenerated artifacts are committed; the `2.1.0` search leaves only justified hits.

Evidence: commit 6fb1c70. Release build with 0 warnings; Packaging 1/1 and Terminal 21/21 passed; `docker compose -f docker/compose.yaml config` shows `jchristn77/arguebot:v2.2.0`. Remaining `2.1.0` hits are CHANGELOG history, README's "v2.1.0 added" line, docs/REQUIREMENTS.md's 2.1 scope, third-party package versions in `assets/dependencies.json` and `dashboard/package-lock.json`, and the Postman collection schema URL (`collection/v2.1.0`).

## Phase 1: Data model and storage (schema 33)

- [x] Tables `resources` (`id`, `tenant_id`, `name`, `name_key` (lowercased), `title`, `description`, `tags_json`, `body`, `locked`, `revision`, `created_by_user_id`, `updated_by_kind` (`user` or `agent`), `updated_by_id`, `created_utc`, `updated_utc`, `UNIQUE(tenant_id, name_key)`) and `resource_revisions` (`id`, `tenant_id`, `resource_id`, `revision`, `name`, `title`, `description`, `body`, `author_kind`, `author_id`, `run_id`, `attempt_id`, `created_utc`). Add both to `SchemaDefinition.cs` and the PostgreSQL schema, with an upgrade step from 32.
- [x] Model `Resource`, `ResourceRevision`, `ResourceSummary` (no body) and request types in `src/ArgueBot.Core/Models`, one type per file, with XML documentation.
- [x] `IResourceMethods` and an implementation in `src/ArgueBot.Core/Database/Implementations/ResourceMethods.cs`: enumerate (paged, filter by tag and text), read by ID or name, create, update with expected revision, rename, lock or unlock, delete, list revisions, read a revision, revert to a revision. Validate the name, sizes and NUL bytes. Return 409 `revision_conflict` on a stale revision and 409 `resource_locked` for agent writes to a locked resource.
- [x] Deleting a tenant deletes its resources and revisions. Revision history is capped (proposal: the last 100 per resource).
- [x] Audit create, update, rename, lock, delete and revert events.
- [x] Docs: data model and retention in `docs/RESOURCES.md` (started here) and the schema change in CHANGELOG.

Acceptance: unit tests on SQLite and PostgreSQL for each method, including tenant isolation (a second tenant cannot read or write by ID or name), revision conflicts, name rules, size limits, cascade on tenant delete and the history cap.

Evidence: commit 1bc71d6. `Runners.Resources` (SQLite, including a schema 32 to 33 upgrade) and `Providers.PostgresqlResources` (a disposable PostgreSQL 127.0.0.1 cluster) pass.

## Phase 2: REST API and API artifacts

- [x] Routes under `/v2.0/api/resources`: `GET` (list), `POST` (create), `GET /{id}`, `PUT /{id}` (update with `revision`), `DELETE /{id}`, `GET /by-name/{name}`, `GET /{id}/revisions`, `GET /{id}/revisions/{revision}`, `POST /{id}/revert`, `POST /import` (one or more markdown files; front matter supplies title, description and tags) and `GET /{id}/export` (markdown with front matter). Member access class.
- [x] Add the operations and DTOs to `assets/api-contract.json`, run `node scripts/generate-api.mjs` and commit the regenerated `assets/postman/openapi.json`, `assets/postman/arguebot.postman_collection.json`, `REST_API.md` and `assets/transport.json`.
- [x] Postman: add resource requests to the collection with saved examples, and a resource journey (create, read by name, update with revision, stale-revision conflict, history, revert, delete) to the v2 acceptance workflow through `scripts/generate-v2-workflow.mjs`.
- [x] Request history keeps no bodies for resource writes larger than the existing body limit.

Acceptance: API tests for every route, including 401 without a token, 404 across tenants and 409 on stale revisions; regenerated artifacts are committed; `scripts/test-v2-postman.mjs` passes with the new resource journey.

Evidence: commit 1bc71d6. `Service.SessionCarriers` runs `ResourceApiChecks` against a real service, and its contract-drift check covers the new routes; `scripts/test-v2-postman.mjs` passed 137 requests and 172 assertions including the resource journey.

## Phase 3: Agent tools

- [x] Add a `ResourceTools` class (or extend `ConversationTools` with resource cases) bound to the invocation's tenant, run, seat and attempt. Tools and arguments:
  - `list_resources` (`tag`, `after`, `limit`): names, titles, descriptions, sizes, revisions, locked flag.
  - `search_resources` (`query`, `limit`): literal case-insensitive search over name, title, description and body, with snippets.
  - `read_resource` (`name`, `offset`, `length` defaulting to 8192 characters, maximum 32768): content with `nextOffset`, `revision` and author marking.
  - `create_resource` (`name`, `title`, `description`, `body`).
  - `update_resource` (`name`, `revision`, `body`, optional `title` and `description`): full replace.
  - `edit_resource` (`name`, `revision`, `find`, `replace`): replaces exactly one occurrence, and fails if `find` matches zero or several times.
  - `append_resource` (`name`, `text`): appends without a revision check, for running notes.
- [x] Give the resource tools their own JSON schemas instead of the shared evidence schema.
- [x] Debate policy gains `resourceAccess` (`off`, `read`, `read_write`) and optional `resourceNames` or `resourceTags` that narrow visibility. Only visible resources can be listed, read or written; others return 404, as if they did not exist.
- [x] Budgets: resource calls count against the existing per-turn tool budget. Add per-turn write limits (proposal: 8 writes and 64 KiB written per turn) and a per-debate cap on resources created (proposal: 50).
- [x] Provenance: record each resource read and write (name, revision) on the attempt, so `debate attempts` shows which context an answer used.
- [x] Wire the definitions into `ConversationEngine` so all three transports offer them (MCP bridge, context envelope, REST tools). Tool activity appears in the transcript like the existing conversation tools.
- [x] `MCP_API.md`: add every resource tool to the tools table with its arguments, results, error codes (`revision_conflict`, `resource_locked`, `not_found`, write limits), the access modes and which tools each mode offers, and the transport notes (MCP bridge, JSON envelope, REST functions). Update the paragraph that lists what the tool server exposes.
- [x] Prompt support: an opt-in `{resources}` placeholder in `PromptCatalog`, and prompt validation that warns when a prompt names `read_resource` with a resource that does not exist in the tenant.

Acceptance: fixture tests for each tool through each transport, using the Test.Fixtures CLI with MCP and envelope modes and a REST fixture. Covers visibility narrowing, locked resources, revision conflicts between two seats, write limits, provenance on the attempt and that `off` offers no resource tools. One live check per CLI (Claude Code and Mux) on an isolated home and a 127.0.0.1 port, recorded in `docs/EXECUTION.md`.

Evidence: commit 0c5841b. `Runners.ResourceTools` (engine with a tool-calling fixture), `Runners.ResourceTransports` (MCP in Claude Code and Codex shapes, JSON envelope) and `Runners.ResourceRest` (native REST tools against the simulation) pass. The live check with Claude Code and Mux is recorded in docs/EXECUTION.md.

## Phase 4: Dashboard

- [x] A Resources page in the sidebar: a table (name, title, description, tags, size, updated, updated by, locked) whose rows open the resource, with search and tag filters.
- [x] A resource view with a markdown editor and preview, plus title, description, tags, locked and rename. Save sends the revision and shows a clear message on conflict.
- [x] A history panel with revision list, agent or user author, diff against the current revision and revert.
- [x] Import (drag-and-drop or file picker for `.md` files) and export.
- [x] The debate setup and execution settings gain resource access and visibility controls. A transcript tool activity row for a resource links to that revision.
- [x] All text goes through i18n (`assets/i18n/en.json`).

Acceptance: component tests for the list, editor conflict handling and history; a simulated-user walkthrough that creates a resource, starts a debate in which an agent reads it and appends a note, then reviews and reverts the note.

Evidence: commit 55e6ebb. `ResourceEditor.test.jsx`, `Resources.test.jsx` and `resources.test.js` pass in `npm run test:ci` (65 tests); `tests/resources.spec.js` passes, and the full Playwright suite passed 47 with 1 environment-gated skip.

## Phase 5: TUI and CLI

- [x] TUI: a Resources screen (list, view, edit in the existing text editor control, history, revert, lock) and resource access in debate setup.
- [x] CLI: `resources list [SEARCH] [--tag T]`, `resources show NAME|ID [--revision N]`, `resources add --file PATH|-`, `resources update NAME|ID --revision N --file PATH|-`, `resources rename`, `resources lock|unlock`, `resources delete`, `resources history`, `resources revert NAME|ID REVISION`, `resources import PATH...` and `resources export NAME|ID`. Add it to the help text.

Acceptance: Terminal suite journeys for the TUI screen; CLI surface tests in `scripts/test-cli-surfaces.mjs`.

Evidence: commit 2908b66. `Terminal.Resources` passes against a real service (22/22 Terminal checks); `scripts/test-cli-surfaces.mjs` passed 99 assertions.

## Phase 6: Documentation, API artifacts and release

- [x] Finish `docs/RESOURCES.md`: what resources are, naming, access modes, locked resources, how to reference them in prompts (with an example role instruction such as "if you need to know more about billing, call `read_resource` with name `billing`"), write limits, history and revert, import and export, and the trust model.
- [x] README: a Resources section under the interface overview, the CLI commands, and the v2.2.0 note from Phase 0.
- [x] `REST_API.md` and `MCP_API.md`: final review against the implemented routes and tools, regenerated where generated.
- [x] Postman: confirm both collections and the OpenAPI document are regenerated from the final contract and the acceptance workflow passes.
- [x] Other docs: `docs/PROMPTS.md` (referencing resources, the `{resources}` placeholder), `docs/EXECUTION.md` (tool budgets, write limits, live checks), `docs/MULTIPLE_AGENTS.md` (debate resource access settings), `docs/TUI.md` (Resources screen), `docs/DEVELOPMENT.md` (new tests), `docs/IDENTITY.md` (resources are tenant-scoped and shared like prompts), `docs/REQUIREMENTS.md`, `docker/README.md` and `DOCKERHUB_README.md` (nothing new to configure, image tag v2.2.0).
- [x] CHANGELOG: complete the `Unreleased: v2.2.0` section (resources, tools, API, dashboard, TUI, CLI, schema 33, breaking changes if any).
- [ ] Build and push the image with `./build-all.sh v2.2.0` once the branch is merged, and update `docker/compose.yaml` only if the tag changes.

Acceptance: every doc link resolves; a search finds no stale references to 2.1.0 as the current version; CHANGELOG lists every user-visible change; the image `jchristn77/arguebot:v2.2.0` is on Docker Hub and `docker compose -f docker/compose.yaml up` starts it.

## Risks

- **Persistent prompt injection.** An agent can write instructions into a resource that a later debate reads. Mitigations: author marking, revision history, locked resources, `read` as the default access, and tool results that present resource text as data.
- **Context growth.** Agents may read large resources repeatedly. Paged reads, the default 8192-character page and per-turn budgets bound it.
- **Concurrent edits.** Seats in the same debate may edit one resource. Revision checks plus `append_resource` for notes avoid lost updates.
- **Name collisions with MCP resources.** The tools are named `*_resource` but are not the MCP resources primitive. `docs/RESOURCES.md` says so to avoid confusion.

## Execution record

Implemented November 5, 2026 on `feature/v2.2.0`:

| Phase | Commit |
| --- | --- |
| 0. Move to v2.2.0 | 6fb1c70 |
| 1 and 2. Storage (schema 33) and REST API | 1bc71d6 |
| 3. Agent tools | 0c5841b |
| 4. Dashboard | 55e6ebb |
| 5. TUI and CLI | 2908b66 |
| 6. Documentation | d39bb6e |
| Test fixes and archive | see below |

Deviations from the plan, each small:

- Names must also start with a letter or digit, so names like `.hidden` or `-x` are rejected.
- One request type, `ResourceRequest`, serves create and update; on update, omitted fields are kept, and rename, lock and unlock go through `PUT` rather than separate routes.
- The extra columns `characters`, `bytes`, `updated_by_name` and `author_name` store sizes and display names; `attempt_resources` holds the per-attempt provenance.
- The MCP frame limit rose from 128 KiB to 1 MiB so a 32,768-character `read_resource` page fits after JSON escaping.
- Agents get `resource_lock_denied` or `resource_rename_denied` (403) when they try to lock, unlock or rename, distinct from `resource_locked` (409) for changing a locked resource.
- Debates created before schema 33 are upgraded with access `off`, so existing debates behave as before; new debates default to `read`.

Two test failures that predated this work were fixed on November 6, 2026, before release:

- `Terminal.StopDelete` with `ARGUEBOT_SIMULATION_URL` chose run-control commands by menu position. "Edit the proposal under vote" and "Change agents" had since moved the Inspect commands down two places, so the journey opened "Change the panel" instead of the proposal reader. It now chooses each command by its label.
- The PostgreSQL provider checks assumed an empty database. The xUnit and NUnit adapters run the .NET 8 and .NET 10 builds against the same `ARGUEBOT_TEST_POSTGRESQL` database, and the second run found the first run's seeded accounts and credentials, encrypted under a key from another home. Each PostgreSQL check now drops the tables it owns in the current schema before it starts, which the variable's "disposable test database" contract allows.

After these fixes, with PostgreSQL, the CLI fixture and the simulation: `Test.Automated` 94/94, and the xUnit and NUnit adapters pass on net8.0 and net10.0 against one database. The plan moved to `archive/` the same day.
