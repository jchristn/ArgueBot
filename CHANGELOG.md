# Changelog

## Unreleased

- Set the product version to 2.0.0.
- Workspace: long turns that arrive as a bounded preview now load and render their full text when shown, and every turn has Open in window for a wide markdown reader (tables included). A live row at the end of the transcript shows which agent is working, on which step, for how long, and its latest visible activity, even when the agent streams nothing. Scrolling up no longer pins the page, so new turns keep arriving; paging back still pins it. Previews keep identifiers such as get_debate_state intact.
- Workspace: a compact Timeline view (now the default) for watching debates: one line per turn with agent color, step, vote position and a preview that follows streaming text, numbered rounds, grouped tool calls, in-place expansion and automatically opened failures. The card view remains a choice. The workspace header, run strip and transcript toolbar take less height, and the start debate dialog is wider.
- Fix Codex turns failing as malformed output when Codex reports a configuration warning: its `error` items carry a plain-text `message`, which the CLI parser now accepts and ignores. The parser also skips other field shapes it does not use, such as the object `result` of Codex tool-call items and web-search results, instead of failing the turn. Space the participant name and ID apart in the generation attempts table.
- Add a debate prompt library (schema 29): read-only factory prompts for the question, response, proposal, vote and summary steps that reproduce the built-in text exactly, plus custom prompts made by duplicating them. Prompts use `{parameters}` validated against a per-step legend. Conversations choose a prompt per step, the start dialog can override the choice, and each debate freezes the chosen text. Available from the dashboard Prompts page, TUI Prompts menu, `prompts` CLI commands and `/v2.0/api/prompts`.
- Conversations have a title and a separate optional opening question (schema 28, `openingQuestion` in the API, `--question` in the CLI, two prompts in the TUI). New conversation asks for both; debates default to the opening question, or the title when it is blank, and the web start dialog shows the question for editing. Updates that omit the opening question keep it.
- Conversations: a Stop debate action (with confirmation) for unfinished debates, delete errors shown inside the delete dialog with a Stop debate and delete option, and notices rendered inside an open dialog instead of beneath it.
- Clicking a conversation row opens the conversation; tab toolbar and settings actions sit flush right; transcript cards are denser, with a stronger edge color per agent, assigned in roster order from a 12-hue palette so agents never share a color (a name hash had given Claude Code and Codex the same one).
- Dashboard density and navigation: tighter spacing across pages, tables, forms and dialogs; a smaller row Actions trigger; a compact request inspector; a 66% taller request chart; Title Case navigation and tab names; collapsible transcript cards with Collapse all, Expand all and Jump to turn; and the message feed as the workspace's only scrolling region (hidden copy-status text no longer stretched the page).
- Add an explicit credential reveal for the moderator: `POST /v2.0/api/configuration/credentials/{id}/reveal`, the client, `configuration credentials reveal ID` in the CLI, Reveal value in the TUI, and a confirmed Reveal value action in the dashboard with show, hide and copy. Request history excludes credential response bodies as well as request bodies; model tools never expose reveal.
- Topbar: the repository link uses the GitHub mark, and the server address shows in a badge.
- Dashboard tables: replace per-row button groups with a single Actions menu that renders above the table, opens above its button near the bottom of the screen and stays inside the viewport, with arrow-key, Escape and outside-click handling. The conversations list shows a Title column and a Rename action.
- Show credential setup commands for macOS, Linux or Windows PowerShell, defaulting to the browser's platform, with a remembered switch; the TUI help uses the host shell.
- Keep primary button text readable on hover in light and dark themes, with an automated hover-contrast check across routes.
- Add bearer token, header key and value, and query string key and value HTTP credential types (schema 27). Header and query credentials store their parameter name, the add dialog adapts to the chosen type, and endpoint bindings that use a typed credential take its placement. Harness logins can no longer be bound to endpoints.
- Web Participants page: split agents, models, CLI harnesses, HTTP endpoints and credentials into URL-linked tabs with tables. Credentials show kind, status and which harnesses and endpoints use them, and the add dialog explains how to obtain a Claude Code token, a dedicated Codex login or an API key, with copyable commands. Harness forms offer only credentials of the matching kind and explain that None uses the service user's own login, which Docker lacks. The TUI credential help gives the same steps.

- Classify provider refusals as `provider_unauthorized` (401/403), `provider_rate_limited` (429, with the provider's Retry-After interval in the message), `provider_unavailable` (5xx), `provider_disconnected` (midstream drop, partial text kept) and `provider_unreachable`, each with recovery guidance. Add 403, 5xx and midstream-disconnect scenarios to the provider simulation, and an invalid refresh response fixture.
- Docker: health checks gain a start period and faster start interval, the published port comes from `ARGUEBOT_PORT`, a secret-free `docker/.env.example` documents provider settings, and the guide states the local-execution trust model.

- Web workspace: show the debate panel beside the transcript with a remembered Show/Hide control, merge each tool call's start and result into one card (also in the TUI), make summary citations open their sources, and show moderator turns as "You". Add parameter and body-field guidance, placeholder and JSON checks to the API Explorer, a clarification dialog component test, and an automated structural accessibility, right-to-left and pseudo-locale review that found and fixed clipped right-to-left sidebar text.

- Credentials: keep a Codex login usable when its refresh request never reaches the login service or is refused with 429 or 5xx (`refresh_unavailable`), and label `refresh_uncertain` as Sign-in needed across the web and TUI. Refuse master-key rotation before changing keys when an enabled credential cannot be decrypted, skip unreadable disabled ones, and add explicit retirement of earlier keys from every interface. Redact only credential-like inherited environment values.

- Complete the model evidence tools: `list_turns` seat and phase filters with a `nextAfter` cursor; literal, case-insensitive `search_context` with match-centered snippets that behaves the same on SQLite and PostgreSQL; `get_supporting_details` for up to 20 IDs with summary source links and explicit gaps; and pinned constraints plus earlier positions in `get_debate_state`. Deduplicate repeated clarification requests within an invocation, and reject summaries that do not attribute a supplied pinned constraint.

- Reject invalid debate setups with field-specific errors (`field` in API errors, CLI output, web and TUI setup), including over-limit panels, repeated profiles and duplicate seat names. Ask a model once for a corrected reply after an invalid question, proposal or vote before pausing (schema 26). Let the moderator edit the proposal under vote from every interface; all seats vote again and votes on the replaced proposal stay as history.

- Scope private directives to their recipient: they no longer advance the shared context epoch, reset the round or interrupt another agent, and a recipient that has not yet seen one votes again before the debate finalizes, with its earlier vote kept as superseded history (schema 25).

- Keep the previous summary active until a moderator rebuild validates (schema 24), and stop a turn that asks a blocking clarification from advancing or voting. Add race, vote-position and roster regression tests.
- Require an explicit delivery choice for every web context submission and clarification answer; show protocol-aware outcomes, proposal access and Home debate status; clarify connection-form help, terminal delivery and proposal views, and reorganize CLI help.

- Fix v2 debate extension after the round limit, interjections lost to concurrent run updates or stored without their run, late output after cancellation, summary-source validation order, shared-only summary rebuild deactivation and startup detection of a missing rotated credential keyring.

- Add manifest-driven self-contained distribution builds for six runtime targets, native macOS/Windows/Linux packaging recipes, checksum-pinned tooling and packaging CI. Verify macOS bundle startup and Linux arm64 package installation, same-version replacement, browser/terminal entry points and data-preserving removal. Windows native execution and publication remain unverified.

- Add explicit inherited environment names to reusable harnesses across web, TUI, CLI and API, freeze selections with participant snapshots, and redact selected values from model output.
- Verify the application container on Linux arm64, including credential injection, encrypted persistence, key rotation and missing-key recovery. Fix the clean dashboard build and loopback Host/origin validation for container port mappings.

- Add reusable model output-capture limits and HTTP generated-token, temperature and top-p overrides, frozen into accepted run snapshots and editable through web, TUI, CLI and API.

- Classify compacted memory into seven explicit sections, expose original hashes and parent summaries across interfaces, and distinguish direct moderator-context delivery from summary-based delivery. Invalid replacement summaries preserve prior memory and original evidence.


- Implement the v2.0.0 preview: configurable multi-agent panels, explicit unanimous ratification, a selected orchestrator model, safe-boundary roster changes, and per-interjection interrupt/next-turn delivery.
- Add source-linked context compaction, scoped evidence tools, blocking moderator clarification and native Claude/Codex MCP invocation bridges.
- Add reusable endpoints, harnesses and models; combined HTTP auth bindings; encrypted write-only credential import; serialized Codex refresh and isolated invocation homes.
- Add independent execution budgets, orchestration guidance, shared endpoint capacity, actual transport-aware context sizing, private summary partitions, paged source manifests, credential revision provenance, versioned master-key rotation and explicit completion/tool verification.
- Expose debate, configuration, memory and recovery workflows in the dashboard, TUI, CLI, native client, REST reference, API Explorer and Postman artifacts. Add an application Docker build and deployment guide.
- Add post-implementation browser and terminal layout checks, four-agent CLI/Postman journeys, and recovery/provider tests. PostgreSQL acceptance passes locally. Linux arm64 container acceptance passes with fictional providers; live-provider and other platform acceptance remain open; see MULTIPLE_AGENTS_PLAN.md.


- Accept successful indexed Claude results that repeat earlier assistant messages after background-agent wakeups, without duplicating text or accepting conflicting replacements for the same result index.

- Distinguish CLI/REST turn deadlines from explicit interruption, stop and service shutdown; expose recovery guidance for interrupted cards, frozen timeouts and live elapsed time, open the matching historical run when inspecting a failed turn, and correlate generation logs with attempt and response IDs.

- Explain I/O failures beside failed answers in the dashboard and TUI, preserve specific output-limit causes across reloads, and recover safe explanations from retained historical diagnostics.
- Preserve the original question when b starts another debate in an existing TUI conversation, keep it editable, and freeze the reviewed question in new debate runs. Ctrl+N still starts blank.

- Default new agent profiles to a 64 MiB output limit and allow limits up to 64 MiB across the service, TUI, dashboard and API contract.

- Put Follow-up directly in the TUI action bar and clarify that the note composer only adds context.

- Start each new TUI conversation with a blank, editable question; label setup in an existing conversation and clarify that its history remains context.

- Render TUI conversation previews and full messages with TUIKit Markdown, including width-aware tables, lists, quotations and code blocks while preserving original message content.

- Fix Claude Code CLI turns failing on valid tool-result payloads; parse assistant text by message ID, deduplicate streamed/final snapshots, and reject empty, errored or incomplete final results. Add sanitized tool-stream replays through the runner and durable service engine.

- Keep TUI wizard button colors consistent with the shared theme after step changes, including focused, hovered and disabled states.

- Retain bounded CLI failure stdout, stderr, triggering events, parser details and exit status in local attempt-linked diagnostic files; add process and durable-engine regressions for evidence retention, limits and diagnostic write failure.

- Show agent activity before response text arrives, distinguish reconnecting updates, and annotate failed dashboard turns with their saved attempt error code.

- Archive the superseded improvement plan, extract remaining work into ROADMAP.md, and update repository instructions, documentation and generated API-reference links.

- Build the native CLI for clean browser-test jobs, use explicit UTF-8 for process stdin and fictional harness consoles, and bound post-shutdown fixture cleanup on Windows.

- Retire the separate npm terminal and browser implementations, their source/build output, root package/dependencies, launchers and CI build. Support the C# TUI and React dashboard through go-tui and go-web; update contributor instructions and documentation accordingly.

- Remove unused pairing/account/session/permission implementations, DTOs, identity-only tests, obsolete planning archives and superseded documentation. Preserve historical database tables and migration safety.

- Remove the external observability stack, collector/export dependencies, configuration and dashboard links; stop scripts now stop only ArgueBot.

- Cache fingerprinted dashboard assets so the browser can reuse logos, scripts and styles; keep HTML and API responses uncached.

- Expand request activity charts with selectable hour/day/week/30-day ranges, exact minute/15-minute/2-hour/8-hour buckets, labeled axes, hover and keyboard details, and explicit status/color legends.

- Remove backup and restore workflows, service jobs/routes, offline server commands and client helpers from all product surfaces.

- Give conversation and participant IDs their own dashboard table columns, retaining monospace text and copy controls.

- Remove product accounts, roles, permissions, security audit and all authentication workflows/routes; keep one open workspace and preserve existing stored data.
- Open existing object editors from table rows with keyboard support; keep explicit copy/action controls independent.
- Add stop-web.sh and stop-web.bat to stop the selected service; make repeated service stops safe.

- Replace the API Explorer operations sidebar with a dropdown above the full-width request and response workspace.

- Close dashboard modals when their backdrop is clicked; preserve inside clicks and drags that begin within the modal.

- Add full-turn, complete-conversation and ID copying with insecure HTTP, permission-denied and manual clipboard fallbacks; render IDs in monospace.
- Redesign request inspection with a metadata grid, separate request/response captures, block copy actions, raw JSON and a responsive wide layout.

- Restore the existing ArgueBot icon in dashboard branding and the browser tab.

- Enable service-owned local file and process tools for REST models, with provider continuation, durable tool history, execution policy, limits and stop cancellation.

- Populate named agent harness executables and complete arguments consistently across web, TUI and API/CLI, including unattended Claude Code and Codex flags.

- Open local workspaces without authentication by default: plain dashboard URLs, no pairing codes, connection fragments, login screen or browser session. Native clients skip enrollment; startup removes old pairing files. Existing owner-backed storage remains intact. The previous authenticated mode is explicit opt-in through RequireAuthentication=true.

- Consolidate web startup under the supported launchers. Fix repeated go-web launches by reusing the verified service for the selected home instead of opening its instance lock again. Preserve custom startup ports and print the existing listener's actual URL when attaching.

- Expand all terminal Help topics into practical guides, including final instructions after consensus, troubleshooting, examples and state-specific recovery. Verify every topic opens, scrolls and returns to the contents at multiple widths.
- Add go-tui.sh/.bat and update go-web.sh/.bat to build and run the C# replacement. Web launchers build current dashboard assets, start or attach to the shared service, and print the actual browser URL. TUI --help now works without an interactive terminal.

- Fix Codex JSONL parsing when a turn emits separate progress and final agent-message items. Track snapshots by item ID, preserve message boundaries and suppress duplicate completions while still rejecting conflicting replacements for the same item. Add real-process regression fixtures and verify both CLI agents in a live local debate.

- Clarify agent setup in terminal and web forms: program-only executables, one flag/value per argument row, complete custom-argument replacement and unchanged historical run snapshots. Catch common executable-plus-flags mistakes without rejecting paths containing spaces. Explain process-launch failures in plain language and guide empty failed answers to the terminal inspector.

- When deletion encounters an unfinished debate, offer Stop debate and delete permanently. Confirming stops the run, waits for completion and deletes automatically; cancellation preserves the conversation, and stop failures never proceed to deletion. Real-service journeys cover active/paused runs and cancelling the combined action.

- Add lowercase s Stop for the highlighted conversation and F8 for the open conversation, with cancellation by default and service completion feedback before deletion. Active-run errors now explain the stop/delete keys. Verify active and paused stop/delete journeys against an isolated real service and hanging fixture harness.

- Preserve lowercase in case-sensitive TUI shortcut hints so displayed keys match accepted input; verify advertised character shortcuts and the rendered delete hint.

- Add focus-scoped conversation shortcuts: d/Delete, b debate, r run controls, p pause/resume, f follow-up, l history, g attempts, a note, n new, e agents and / search. List actions target the highlighted row, deletion defaults to Cancel, and the footer advertises local actions. Alt+1/2/3 focuses conversations/history/notes, with keyboard scrolling and full-history access from the history pane; Escape returns to conversations. Move the global follow-up shortcut to Ctrl+Y so Ctrl+A remains available for input selection. Menus now show global shortcuts.

- Apply Quiet Charcoal to the terminal with soft text, muted blue focus and charcoal inputs. Remove workspace boxes, retain a single sidebar divider and keep dialogs framed.

- Add Mux CLI harness support, a terminal preset and web protocol selection, with stdin prompts, streamed text capture and nonzero-exit handling.

- Schema 13 seeds protected owned-access and tenant-presence assignments, synchronizes identity changes atomically, preserves assignment IDs during recovery and labels system migration audit events clearly. Complete scoped domain enforcement remains in progress.

### In progress

- TUI interaction audit: F1 opens context help inside dialogs, Escape closes dropdowns before cancelling forms, menu labels wrap and retain option identity, the footer shows complete essential controls, setup steps scroll, and read-only help/message/review views start at the beginning with working keyboard/mouse scrolling. Welcome guidance stays at the top on narrow screens.
- Conversation navigation opens on one click, rejects stale history replies and keeps note drafts separate. Debate submission stays on Review when the service fails and blocks duplicate pending starts. Permanent conversation deletion is now in Menu, defaults to Cancel and respects active/paused-run refusal.

- The C# TUI now exposes clickable Help, Menu, New, Agents, Start debate and Run controls buttons. Menu options, prompt fields and scrollable form controls now forward mouse input, including Save buttons. Empty workspaces explain the next steps, a separate line shows connection feedback, and the open conversation is marked in the list.

- Debate setup is a four-step wizard: Question, Speakers, Settings and Review. Back preserves drafts, validation blocks invalid transitions, and only the final Start debate submits the conversation/run. Missing agents lead to setup with Claude Code, Codex, Gemini, REST and custom CLI presets. F2 opens the main action menu; notes are explicitly described as context rather than agent requests.

- F1 Help leads the TUI footer and opens a topic menu for navigation, agent configuration, debate controls, history, administration and theme/detach shortcuts.

- TUI dark mode uses black workspace/dialog backgrounds and dark grey inputs consistently. Bordered workspace regions have horizontal padding; prompts, choices and confirmations share the same theme and dialog layout.

- TUI conversation messages use styled speaker headings, local times and spaced separators, with message content directly beneath each heading. Routine completed/note metadata is hidden; interrupted/failed states and preview notices remain visible. Full-message and history headings use the same readable labels.

- The C# TUI opens with a lowercase Ogre ASCII logo in a centered splash modal over the workspace. The modal separates text groups with blank rows and keeps padding below its dismissal hint. It includes version, copyright, license, project URL and configuration/help shortcuts. Any key dismisses it immediately; otherwise it closes after three seconds.

- Scoped user-role assignment APIs and console lifecycle, shared GUID/name/built-in resolution, protected identity/assignment checks, exact resource scope and bounded tenant inheritance. Creation/reactivation enforce the 256-active-assignment limit. Complete domain enforcement and remaining editors are still in progress.

- Tenant permission-definition APIs and console commands, plus revision-checked role-permission replacement and active/inactive mapping inspection. Explicit denies, independent activity, protected definitions, stable historical mapping IDs and atomic audit events are covered on both databases. Remaining editors, assignments and complete route enforcement are in progress.

- Dashboard Roles page and Ctrl+O terminal workflow for protected built-in inspection, custom-role cloning and revision-checked editing/deactivation. Web conflicts preserve drafts; ordinary users cannot expose management controls through direct page navigation.

- Authenticated role definition browsing, tenant custom-role creation/cloning and revision-checked deactivation. Built-ins remain immutable, current database administrator authority is rechecked, and changes carry atomic audit events. Permission/assignment editing and complete scoped enforcement remain in progress.

- Schema 12 stores protected built-in roles, tenant permissions, mappings and scoped user assignments. Current-user resolution enforces activity, name fallback, exact scope and deny precedence. Management endpoints and route enforcement remain in progress.

- Schema 11 replaces stored password equivalents with salted PBKDF2-HMAC-SHA256 verifiers. Existing accounts and sessions migrate without password changes; stored verifiers cannot be replayed as login input.

- Scoped authorization evaluator and shared truth-table coverage for explicit deny, inheritance, ownership narrowing, administrator bypass and credential owner ceilings. Stored role management and endpoint integration remain in progress.

- Background generation now checks current identity execution authority before attempts and during streaming. Logout leaves accepted work running; account disablement or role revocation interrupts it, preserves provenance and pauses without replay. Denials have independent audit and bounded metrics.

- Optional loopback HTTPS with operator-provisioned PFX and password environment reference. Password exchange requires TLS; native local trust can use an exact protected home/endpoint certificate pin while retaining name and validity checks. TLS private keys require encrypted opt-in recovery.

- Tenant user administration across dashboard, terminal and console; tenant-bound password exchange; protected local owner; optimistic identity updates and atomic session revocation. Schema 10 preserves older identity records without assigning passwords. Hosted credential/RBAC/isolation gates remain open.
- URL-encoded query filters now decode through Watson's query accessor, restoring email, Unicode and punctuation searches.

- Domain conversation/profile reads now reject unauthenticated or unbound principals even when called outside HTTP, with shared checks on both supported databases.

- Configurable incremental event retention, bounded replay pages, typed cursor recovery and native event streaming. Dashboard reconnects refresh snapshots after expired cursors. Large stream callbacks are bounded and malformed Unicode preserves the valid prefix before failing the attempt.

- Shared C# conversation runtime, durable application home and configurable runner architecture. The TUI, CLI and React dashboard share this service. See ROADMAP.md for remaining delivery and acceptance work.

- React dashboard with persistent conversation navigation, participant configuration, streamed workspace updates, follow-up questions, request-history inspection, API Explorer, and settings/restart controls. Dark/light icon controls and hover help remain covered by browser checks.
- Shared SQLite/PostgreSQL driver structure, historical SQLite backup/upgrade checks, AES-GCM session protection and immediate revocation. Full hosted identity administration remains pending. Provider scope follows the user's SQLite/PostgreSQL-only instruction.
- TUIKit debate and moderator controls, search/paging, follow-ups, focus audit and theme regression tests; direct process capture now terminates promptly on malformed or oversized output.
- Generated REST/OpenAPI/Postman artifacts, cross-platform CI, and a provisioned local metrics/traces/logs stack with rotating native logs. These additions do not declare the replacement ready for cutover.
- Local console/TUI startup and attachment, owner-readable service metadata, concurrent pairing recovery, and authenticated graceful service stop/restart commands.
- Ordered migration tracking, portable checksummed database snapshots, pre-upgrade PostgreSQL snapshots, and offline transactional restore with corruption/rollback and PostgreSQL-to-SQLite tests. Complete recovery archives add optional authenticated encryption, explicit secret inclusion, dependency provenance, bounded service-owned jobs, authenticated download/verify/delete, and isolated SQLite-home restore. Complete-bundle restore also supports explicitly provisioned empty PostgreSQL destinations, retaining protected operator staging after post-commit publication failure.
- Database-level ownership now protects SQLite files across separately configured homes and PostgreSQL through a dedicated advisory-lock writer; lost PostgreSQL ownership signals service shutdown and never reconnects automatically.
- Session privileges now use separate stored system/tenant administrator flags. Historical snapshots retain versioned column inventories and migrate before reuse.
- Stream commits now batch by size and timer, flush before finalization, preserve split Unicode surrogate pairs, and record committed text offsets. Countdown/typing holds use injectable monotonic time.
- TUI participant forms now create, edit and delete reusable CLI/REST configurations, preserve ordered empty arguments and keep drafts visible after service save errors.
- Recovery now includes paginated durable-job browsing in web/console/TUI, masked terminal recovery forms, and headless frame evidence in runtime CI. Console participant update/delete commands complement the existing create/list commands.
- Windows background service launches restrict inherited handles to the null device, preventing service lifetime from retaining client capture pipes. Cross-platform CLI smoke now uses a home with spaces and Unicode.
- Local clients verify port-bound home proofs before reusing owner-protected CLI sessions; frequent commands retain enrollment rate limits and current-privilege checks. Offline snapshot restoration revokes recovered sessions.
- Session authentication rejects unsupported mixed schemes and duplicate/conflicting tenant or token headers. Append-only security accounting records issuance/revocation atomically and HTTP denials independently of request capture, with scoped inspection in web, console and terminal.
- Generation attempts now preserve exact source-message identifiers, prompt version/digest, immutable questions and measured duration. Acceptance/finalization checkpoints and successful directive consumption are atomic; interrupted directives survive explicit retries. Character/message limits pause before invocation, with provenance inspection across web/CLI/TUI and real fixture debate journeys. A pause during the final answer no longer strands an exhausted run.
- Model-context preparation now bounds metadata and payload reads before loading source content, and completed message content cannot be appended or rewound. Exact Unicode and recipient boundaries are tested on SQLite and PostgreSQL.
- Attachment protocol 2 introduces bounded message pages and explicit full reads. Web/TUI polling uses previews, older-page membership remains stable during appends, and exports hydrate full text. Native JSONL and Markdown exports stream complete messages; Ctrl+L opens terminal history without conflicting with Backspace. Client/service binaries must be upgraded together; the product version is unchanged.
