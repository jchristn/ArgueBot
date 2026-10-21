# Changelog

## Unreleased

- Remove unused pairing/account/session/permission implementations, DTOs, identity-only tests, obsolete planning archives and superseded documentation. Preserve historical database tables and migration safety; keep the legacy application runnable pending cutover.

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

- Remove the extra legacy web launchers at user request. Fix repeated go-web launches by reusing the verified service for the selected home instead of opening its instance lock again. Preserve custom startup ports and print the existing listener's actual URL when attaching.

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

- Shared C# conversation runtime, durable application home and configurable runner architecture. The existing TypeScript entry points remain supported during migration. See IMPROVEMENTS.md for delivery gates and verified progress.

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
