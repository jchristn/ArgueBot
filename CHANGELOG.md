# Changelog

## Unreleased

### In progress

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
