# User authentication and multitenancy plan (v2.1.0)

Status: approved for execution on `feature/v2.1.0`. Target version: 2.1.0 (version change approved by the product owner on November 4, 2026). Prepared November 4, 2026.

This plan adds tenants, users, bearer-token credentials, a login page, authenticated APIs and time-limited public share links to ArgueBot, which shipped v2.0.0 as one open local workspace. It also moves the Docker deployment to published images and adds image build scripts. Each phase lists its tasks, the files involved and the evidence required before its box is checked. Do not check a box without that evidence.

## Decisions

These decisions come from the product owner and override any conflicting default in the requirements.

| Topic | Decision |
| --- | --- |
| Multitenancy | Yes. Every record belongs to exactly one tenant. Requests resolve to one tenant before any data access. |
| RBAC | Not in scope. No roles, permissions or assignments. Authorization uses the `IsAdmin` and `IsTenantAdmin` flags from AUTHENTICATION.md. |
| User types | Three, from two flags on each user. **System administrator** (`isAdmin`): creates, edits and deletes anything, including tenants, any tenant's users (with `x-tenant-id`), service settings and encryption keys. **Tenant administrator** (`isTenantAdmin`): creates, edits and deletes anything inside their own tenant, including its users, their tokens and sessions, request history and the audit log. **User** (neither flag): uses and changes the tenant's shared conversations and configuration, manages only their own account, tokens and sessions, and has no administration. A system administrator is always also a tenant administrator. Only a system administrator can grant or remove system administration. The dashboard Users page sets both flags; the role column shows the result. |
| Sharing inside a tenant | Shared. Every user in a tenant sees and edits the tenant's conversations, agents, models, harnesses, endpoints, secrets and prompts. `user_id` on a record means "created by", not "owned by". |
| Naming | **Credentials** are the new user bearer tokens, matching AUTHENTICATION.md. The existing provider secrets (Claude Code tokens, Codex logins, HTTP API keys) become **Secrets** in the UI, CLI, TUI and API. |
| Default identity | On every startup, ensure a default tenant, a default user `admin@arguebot` with password `password`, and a credential whose bearer token is `arguebotadmin`. Never overwrite them once a user has changed them. |
| Login screen | Always shows the default credentials (email, password and token), with a note to change them. |
| Public sharing | Each conversation can have time-limited, revocable, read-only public links that open the transcript reader without signing in. |
| Images | `docker/compose.yaml` uses the published image `jchristn77/arguebot:v2.1.0` instead of a build context. `build-arguebot.sh` and `build-arguebot.bat` build and push it. |
| Version | 2.1.0, with CHANGELOG entries under Unreleased. |

## Requirements compliance

The normative requirements are in `~/Code/Agents/requirements`; reviewed hashes are in `docs/requirements-manifest.json` and match the current files. This plan follows AUTHENTICATION.md except where the decisions above say otherwise. Record each deviation in `docs/REQUIREMENTS.md` and `docs/IDENTITY.md`:

- **No RBAC.** The role, permission, role-map, assignment and credential-scope tables are not used. Bypass rules from "Authorization Bypass Rules" decide access: `IsAdmin` users manage all tenants; `IsTenantAdmin` users manage their own tenant's users, credentials and sessions; every other active user has full data-plane access inside their tenant and no administrative access. Admin endpoints never fall back to member access ("Admin Endpoint Hardening").
- **No accounts or administrators tables.** System administration uses users with `IsAdmin`.
- **Bearer-token credentials only.** Credentials use the `DirectHeader` mode with a single bearer token. Access-key and secret-key pairs and signed requests are out of scope. Tokens are stored as SHA-256 hashes with the last four characters kept for display.
- **Password storage.** Passwords keep the existing salted PBKDF2-SHA256 verifier (`password_salt`, `password_iterations`), which is stronger than the plain SHA-256 hash the requirement shows. Comparison is constant-time.
- **Token routes.** Session routes live under the product's API root (`/v2.0/api/token`) instead of `/v1.0/token`.

Everything else applies as written: tenant resolution and conflict rejection, one scheme per request, `Authorization: Bearer` and `x-token` as equal carriers, opaque AES-256 session tokens with a random IV per token, server-side revocable sessions, active-flag checks on tenant, user, credential and session, protected records, cascading deletes, constant-time comparison, rate limiting on authentication routes, audit of authentication outcomes, session changes, credential changes and denials, and the response codes table.

## Current state

- `tenants`, `users`, `auth_sessions` and `audit_events` tables survive from earlier work. `users` already has email, salted password fields, `is_admin`, `is_tenant_admin`, `protected` and `revision`.
- Startup seeds tenant `ten_local` and user `usr_local` (`DatabaseDriverBase.InitializeCoreAsync`).
- Every request gets a fixed admin context in `ArgueBotServer` (`Routes.AuthenticateRequest`), and startup work uses another fixed context (`workspaceContext`).
- Data methods already filter by `tenant_id`. Conversation queries also filter by `user_id`, which must change to tenant-wide reads.
- `LocalHttpPolicy` accepts only `Host: 127.0.0.1`, which blocks remote use even behind authentication.
- The dashboard, CLI and TUI send no authentication. The dashboard streams events with `fetch`, so it can send headers.
- Provider secrets live in `connection_credentials` and are exposed at `/v2.0/api/configuration/credentials`.

## Phase 0: Branch, housekeeping and images

- [x] Create `feature/v2.1.0` from `main`.
- [x] Set the version to 2.1.0 in `Directory.Build.props`, `dashboard/package.json`, `dashboard/package-lock.json`, `scripts/generate-api.mjs`, `dashboard/src/App.jsx` (sidebar), `ProductTelemetry.cs`, `SplashScreen.cs`, `TerminalSuites.cs` (splash assertion) and the `packages.lock.json` and `packaging/locks` project references. Regenerate API artifacts.
- [x] Move `MULTIPLE_AGENTS_PLAN.md` to `archive/MULTIPLE_AGENTS_PLAN.md` and fix every link to it.
- [x] Bring `DOCKERHUB_README.md` up to date with v2.0.0 (what the image is, Compose usage, volumes, ports, health, environment). Phase 7 updates it again for sign-in.
- [x] Change `docker/compose.yaml` to `image: jchristn77/arguebot:v2.1.0` with no `build` section. Keep a commented build alternative for local development, or a `compose.build.yaml` override, so the container acceptance script can still test a local image.
- [x] Add `build-arguebot.sh` and `build-arguebot.bat` following the pattern in `~/Code/Armada/build-admiral.*`: require a tag argument, `docker buildx build` on builder `cloud-jchristn77-jchristn77` for `linux/amd64,linux/arm64/v8`, tag `jchristn77/arguebot:latest` and `:<tag>`, `-f docker/app/Dockerfile`, push, then pull both tags. Add `build-all.sh` and `build-all.bat` that call `build-arguebot` and point to `build-installers`.
- [x] Update `docker/update.sh` and `docker/update.bat` to pull the published image instead of building one, and `scripts/test-container.mjs` and CI to use `docker/compose.build.yaml`.
- [x] Docs: README (Docker section and build scripts), `docker/README.md`, CHANGELOG.

Acceptance: `dotnet build` with no warnings; the Terminal suite passes with the new splash version; `docker compose -f docker/compose.yaml config` shows the named image; `bash -n` passes on the new scripts; no remaining links to the old plan path.

## Phase 1: Schema and identity data (schema 30)

Migration 30 extends the existing tables in place; it must be idempotent and safe on SQLite and PostgreSQL.

- [x] `tenants`: add `protected`, `revision`. The seeded tenant keeps ID `ten_local` and is renamed "Default tenant" with `protected = 1`. All existing data already belongs to it.
- [x] `users`: add `first_name`, `last_name` (copy `name` into `first_name`); keep `email`, password fields, `is_admin`, `is_tenant_admin`, `active`, `protected`, `revision`. Enforce a unique index on `(tenant_id, lower(email))`.
- [x] New `auth_credentials` table: `id`, `tenant_id`, `user_id`, `name`, `token_sha256`, `token_last4`, `auth_mode` (`DirectHeader`), `active`, `protected`, `expires_utc`, `last_used_utc`, `revision`, `created_utc`, `updated_utc`. Unique index on `token_sha256`.
- [x] `auth_sessions`: add `credential_id`, `principal_type` (`User` or `Credential`), `auth_scheme`, `token_id`, `source_ip`, `user_agent`, `last_used_utc`, `revoked_utc`, `revocation_reason`, `protected`.
- [x] `audit_events`: confirm it holds the fields in "Audit Record Fields"; add any missing ones (`principal_type`, `credential_id`, `request_type`, `method`, `path`, `source_ip`, `authentication_result`, `authorization_result`, `reason`, `status_code`).
- [x] New `conversation_shares` table for Phase 5: `id`, `tenant_id`, `conversation_id`, `created_by_user_id`, `token_sha256`, `token_last4`, `include_tools`, `expires_utc`, `revoked_utc`, `last_viewed_utc`, `view_count`, `active`, `created_utc`, `updated_utc`.
- [x] Seeding (every startup, inside the initialization transaction, skipped for empty restore targets as factory prompts are): ensure `ten_local`; ensure user `usr_local` with email `admin@arguebot`, first name `Default`, last name `Admin`, `is_admin = 1`, `is_tenant_admin = 1`, `protected = 1`, and password `password` only when no password is set; ensure credential `cred_default_admin` named "Default admin token" for that user with the hash of `arguebotadmin`, `protected = 1`, only when it does not exist. Never reactivate or reset a record the user changed.
- [x] `BackupSchema`: introduced versions for new tables and columns; update the backup fixture that rewrites old snapshots.
- [x] Core models (one type per file, XML docs): `Tenant`, `TenantRequest`, `User` (no password fields), `UserRequest`, `PasswordChangeRequest`, `AuthCredential`, `AuthCredentialRequest`, `AuthCredentialCreated` (includes the raw token once). `AuthSession` moves to Phase 2 and `ConversationShare`, `ConversationShareCreated` to Phase 5, where they are first used.
- [x] Tenant, user and credential methods behind one interface, `IIdentityMethods` (`DatabaseDriverBase.Identity`). Every method takes `RequestContext`, filters by tenant and applies the bypass flags. Session methods move to Phase 2, share methods to Phase 5 and audit queries to Phase 3.
- [x] Without RBAC, `HasWritePermission` is true for every authenticated context; the obsolete viewer-role assertion became an unauthenticated-mutation assertion.

Acceptance: a new console test proves seeding is idempotent, survives a restart and never resets a changed password or renamed tenant; protected defaults cannot be deleted or disabled; migration from schema 29 on SQLite and PostgreSQL keeps existing conversations in `ten_local`.

Evidence (2026-11-04): `Runners / Identity` (SQLite, including restart) and `Providers / PostgresqlIdentity` pass. The full console run passes 70 of 70 runnable checks (14 skipped for missing external services), and `scripts/test-postgresql.mjs` passes 4 of 4. The backup suite restores a rewritten schema 4 snapshot. Fixed along the way: backup column discovery skipped names containing digits (`password_sha256`, `token_sha256`, `token_last4`), so those columns were never exported; snapshots before schema 30 are gated accordingly.



## Phase 2: Authentication pipeline

- [x] Replace the fixed context in `ArgueBotServer.Routes.AuthenticateRequest` with a real authenticator: `ArgueBot.Core/Services/RequestAuthenticator.cs` (testable without the server) behind `ArgueBot.Server/Services/RequestGate.cs`. It reads exactly one scheme:
  - `Authorization: Bearer <token>` or `x-token` (both allowed only when equal; otherwise 401 `conflicting_credentials`). Any other `Authorization` scheme is 401 `unsupported_scheme`.
  - The token is either a session token (opaque AES-256-GCM with a random 96-bit nonce per token, which authenticates as well as encrypts; prefix `abs_`; key in `secrets/session.key`) or a credential bearer token (looked up by SHA-256 and compared in constant time).
  - Login headers `x-email`, `x-password` and optional `x-tenant-id` are accepted only on `POST /v2.0/api/token`; `x-password` anywhere else is 401 `password_not_accepted`.
- [x] Session tokens carry session ID, principal type, user ID, credential ID, tenant ID, issued and expiry times and a nonce. Validation checks the session row, active and not revoked, the expiry, and that the tenant, user and credential are still active.
- [x] Interactive sessions last 60 minutes and slide on use (`POST /v2.0/api/token/refresh`). Credential bearer tokens authenticate directly and update `last_used_utc` at most once a minute.
- [x] Tenant resolution: from the token or credential. `x-tenant-id`, if sent, must match, otherwise 403 `tenant_mismatch`; a system administrator may use it on tenant-admin routes to act in another tenant (audited as `AuthorizationBypass`). At login the password is checked against every account with that email; if it matches in exactly one active tenant that one is used, otherwise 409 `tenant_required` lists only the tenants whose account accepted the password, so the list reveals nothing to a wrong password.
- [x] `RequestContext` gains `PrincipalType`, `SessionId`, `CredentialId`, `AuthScheme` and `SourceIp` (added in Phase 1).
- [x] The authenticator sets `IsAuthenticated`, `IsAdmin` and `IsTenantAdmin` from the user.
- [x] `AuthSession` model and session methods on `IIdentityMethods`: issue, validate, refresh, list own and a user's sessions, and revoke one or all (`RevokeSessionsAsync` already exists for password changes and deletes).
- [x] Route classes, in one static map (`ArgueBot.Core/Services/RouteAccess.cs`, prefix rules), checked against every operation in the OpenAPI document by the real-service test, so every new route must choose a class:
  - **Public:** `/health`, dashboard static files, `/v2.0/api/token` (POST), `/v2.0/api/login-info`, the API contract and OpenAPI documents, `/share/*` pages and `GET /v2.0/api/shares/{token}`.
  - **Member:** all existing conversation, debate, configuration, prompt, history and explorer routes.
  - **Tenant admin:** user, credential and session management for the caller's tenant; tenant audit.
  - **System admin:** tenant management, service settings and restart, encryption key rotation and retirement.
- [x] Unauthenticated calls to non-public routes return 401 `authentication_required`; authenticated callers without the right class get 403 `forbidden`, with `reason`, `requiredPermission` and `requestType` as in "Authorization Failure Response".
- [x] Replace the loopback-only `LocalHttpPolicy` with a host allowlist: `127.0.0.1` and `localhost` always, plus names from the new `AllowedHosts` setting (`ARGUEBOT_ALLOWED_HOSTS`, comma-separated). Origins must match an allowed host. Document that remote use needs TLS or a TLS-terminating proxy.
- [x] Sign-in is rate limited per source IP and per email (10 failed attempts per 5 minutes; a success clears the email), returning 429 `rate_limited` with `Retry-After`.
- [x] Audit: login success and failure (without the password), session issue, refresh, revoke and expiry, credential create, revoke and first use per day, tenant and user changes, every 401 and 403, and admin bypasses for cross-tenant actions.
- [x] Remove the remaining fixed `ten_local` contexts: startup work uses an explicit system context; background runs keep the tenant and user of the request that started or resumed them, and `RunnerProfile.RuntimeTenantId` makes the runner resolve endpoints and secrets inside the run's tenant.
- [x] Data visibility: remove `user_id = @user` filters from conversation reads, searches and updates so every member of a tenant sees the tenant's conversations. Keep `user_id` as the creator.
- [x] The model tool server (MCP and REST tools) stays invocation-scoped: it reads only the run's conversation inside the run's tenant and needs no bearer token.

Acceptance: console tests for each scheme, conflicting carriers, wrong-tenant headers, expired, revoked and disabled sessions, credentials, users and tenants, cross-tenant reads and writes on every resource type returning 404, rate limiting, audit rows, and the host allowlist; PostgreSQL runs the same checks.

Evidence (2026-11-04): `Service / SessionCarriers` (real TLS service: every carrier and failure code, host and origin allowlist, members, credentials, sessions, password change, disabled users and tenants, a second tenant with cross-tenant 404s for conversations, runs, messages, secrets, endpoints and prompts, tenant cascade delete, audit contents, rate limiting and OpenAPI drift) and `AuthenticatorChecks` with a controlled clock (expiry, refresh, revocation, tampered and foreign tokens, credential expiry, tenant choice, disabled tenants, rate-limit window) on SQLite (`Runners / Identity`) and PostgreSQL (`Providers / PostgresqlIdentity`). Full console run 70 of 70 runnable; PostgreSQL 4 of 4; CLI surface 57 assertions (68 with the mock provider journey).

## Phase 3: Management API

All routes are under `/v2.0/api`, use PrettyId string IDs, return field-specific `ApiError` codes, and cascade deletes.

- [x] Session: `POST /token` (login with headers or JSON `{ email, password, tenantId? }`, returns `{ token, expiresUtc, user, tenant }`), `POST /token/refresh`, `GET /token` (validate), `GET /token/details`, `DELETE /token` (logout).
- [x] `GET /login-info` (public): product version, whether a tenant must be chosen, and the default credentials text for the login page.
- [x] `GET /me`, `PUT /me` (names), `PUT /me/password` (current and new password; revokes the caller's other sessions).
- [x] Credentials (own): `GET /me/credentials`, `POST /me/credentials` (name, optional expiry; returns the raw token once), `PUT /me/credentials/{id}` (rename, enable or disable), `DELETE /me/credentials/{id}`.
- [x] Sessions (own): `GET /me/sessions`, `DELETE /me/sessions/{id}`.
- [x] Tenant admin: `GET|POST /users`, `GET|PUT|DELETE /users/{id}`, `PUT /users/{id}/password`, `GET|POST|DELETE /users/{id}/credentials[/{credentialId}]`, `GET /users/{id}/sessions`, `DELETE /sessions/{id}`, `GET /audit` (filtered, paged, retention delete).
- [x] System admin: `GET|POST /tenants`, `GET|PUT|DELETE /tenants/{id}`. Deleting a tenant cascades to everything in it and requires the tenant name as confirmation. Protected tenants, users and credentials cannot be deleted or disabled through the API.
- [x] Rename provider secrets: `/v2.0/api/configuration/secrets` (list, create, replace, disable, reveal) and `/configuration/secret-keys` (encryption keys). Keep the old `/configuration/credentials` routes as deprecated aliases for 2.1.x, marked in the contract and REST_API.
- [x] Contract: every operation in `assets/api-contract.json` declares its access class and a bearer security requirement (none for public routes); `generate-api.mjs` emits the OpenAPI `bearerAuth` scheme and per-operation security.

Acceptance: contract drift check passes; console and CLI tests cover each route's success, validation, 401, 403 and cross-tenant 404 behavior; raw credential tokens never appear in any later response or in request history.

Evidence (2026-11-04): `node scripts/generate-api.mjs` emits 131 operations with `x-arguebot-access`, `bearerAuth` and `xToken` schemes, 401 responses, `AuthorizationError` 403 bodies and deprecated aliases. The real-service test reads the served OpenAPI document and confirms every operation's access class matches the gate, every admin route refuses a member with 403 naming its class, and every GET route is served. Request history excludes bodies for sign-in, token, credential, secret, share, user and password routes. CLI commands for the new routes land in Phase 6.

## Phase 4: Dashboard

Follow FRONTEND_ARCHITECTURE.md, DASHBOARD_STYLE_AND_USABILITY.md and I18N.md (every string in `assets/i18n/en.json`).

- [x] Login page at `/dashboard/login`, outside the app shell: email, password, a tenant selector shown only after `tenant_required`, and "Sign in with a token" as an alternative. A panel always shows the default credentials (`admin@arguebot`, `password`, token `arguebotadmin`) with a note to change them after signing in.
- [x] Session handling: keep the session token in `sessionStorage` (cleared when the browser closes; never in `localStorage` or cookies), send `Authorization: Bearer` on every request including the event stream, refresh before expiry, and redirect to the login page on 401 with a return path.
- [x] Top bar: signed-in user and tenant badge with a menu: Account, Sign out.
- [x] Account page: profile, change password, my credentials (create with the token shown once and a copy button, list with last four characters, last used and expiry, rename, disable, delete), my sessions (sign out other sessions).
- [x] Administration section (tenant admins and system admins): Users (create, edit, disable, reset password, manage a user's credentials), Audit log. System admins also see Tenants (create, rename, disable, delete with confirmation) and keep Settings; other users no longer see Settings or encryption-key controls.
- [x] Participants: rename the Credentials tab to **Secrets**, including dialogs, help text, harness and endpoint pickers, and the encryption-keys dialog.
- [x] UX pass at the end of the phase: light and dark themes, 390, 768 and 1280 px widths, keyboard and screen-reader names, right-to-left and expanded pseudo-locales, hover contrast, empty and error states.

- [x] System administrators pick any tenant on the Users page (Tenants > open a tenant), which sends `x-tenant-id`; this is how a new tenant gets its first tenant administrator.

Acceptance: browser tests for login (default credentials visible), wrong password, tenant selection, sign in with a token, logout, expiry redirect, account password change, credential shown once, user administration, tenant administration as system admin, 403 handling for members, and the renamed Secrets tab; accessibility and hover-contrast suites pass.

Evidence (2026-11-04): `tests/sign-in.spec.js` (default credentials always visible, wrong password, return to the requested page, reload keeps the session, sign out, bearer-token sign-in, member navigation without administration and a redirect away from Settings, a token created on the Account page shown once and then disabled, user creation, audit rows, tenant creation with its first tenant administrator, tenant choice for a shared email, password change, revoked session returning to sign-in with a reason, and the sign-in page at 390 and 1280 px in both themes and right-to-left). The accessibility, hover-contrast and route review specs now include Account, Users, Tenants and Audit. Component tests cover the session module (sessionStorage only, bearer header, restore, tenant choice, 401 handling, refresh five minutes before expiry, server sign-out), return paths and role navigation: 54 of 54. Full browser suite: 43 passed, 1 skipped, apart from the intermittent navigation error recorded in Phase 8.

## Phase 5: Public share links

- [x] API (member access unless stated): `GET /conversations/{id}/shares`, `POST /conversations/{id}/shares` (`expiresInHours` from 1 to 2160, default 168; `includeToolCalls`, default false; returns the URL and raw token once), `DELETE /conversations/{id}/shares/{shareId}` (revoke).
- [x] Public read: `GET /v2.0/api/shares/{token}` returns the transcript bundle the reader needs (conversation title and opening question, runs with participants and outcome, messages with complete text, proposals and votes). It never returns provider secrets, prompt bodies, attempts, runner diagnostics or request history, and omits tool messages unless `includeToolCalls` was chosen. Expired, revoked, unknown or disabled-tenant links all return the same 404 `share_not_found`. Requests are rate limited per IP, and each view updates `last_viewed_utc` and `view_count`.
- [x] `ConversationShare` and `ConversationShareCreated` models and share methods (list, create, revoke, resolve by token hash), using the schema 30 `conversation_shares` table.
- [x] Share tokens are 32 random bytes, base64url, stored as SHA-256. Deleting a conversation deletes its shares; disabling the tenant disables them.
- [x] Public page `/share/{token}`: the transcript reader in a read-only public layout (no app shell, no edit controls, a banner with the expiry time and "Shared from ArgueBot"), with Copy as Markdown and Print.
- [x] Workspace: a **Share** button in the conversation header opens a dialog to create a link (expiry choices: 1 hour, 1 day, 7 days, 30 days, 90 days; include tool calls), copy it, and list or revoke existing links with their expiry and view counts. The conversation's Actions menu gets "Share" too.
- [ ] TUI: run controls or the conversation menu offers Share (create, list, revoke, copy the URL into a panel). CLI: `shares list|create|revoke`.
- [x] Share URLs use the request's allowed host, so links created through `view.example.com` point there.

Acceptance: console tests for expiry, revocation, cross-tenant isolation and redaction (no tool output when excluded; never secrets or attempts); browser tests that create a link, open it in a fresh browser context without signing in, see the reader, then see "not found" after revocation and after expiry (using a clock override).

Evidence (2026-11-04): `ShareChecks` on SQLite and PostgreSQL (tool turns only when chosen, private directives never, view counts, revocation, unknown tokens, other tenants, deletion with the conversation); the real-service test (shown once, read without sign-in, `Referrer-Policy: no-referrer` and `noindex` on the page, no token in lists or request history, which stores `/v2.0/api/shares/[token]`, cross-tenant 404s, revocation); `tests/share.spec.js` (Share dialog in the workspace, link opened in a signed-out browser with the read-only banner and no account controls, revocation). Public reads are limited to 120 per minute per address. The TUI and CLI share commands are delivered with Phase 6.

## Phase 6: CLI and TUI

- [ ] Client: `ArgueBotClient` accepts a bearer token and sends it on every request and event stream; it surfaces 401 as `authentication_required`.
- [ ] CLI: `--token TOKEN` and `ARGUEBOT_TOKEN`; `login [--email E] [--tenant T]` (prompts for the password without echo and saves a session token to a 0600 file in the application home); `logout`; `whoami`; `me password`; `credentials list|create|disable|delete`; `users ...`, `tenants ...` and `audit` for admins; `secrets ...` replacing `configuration credentials ...` (old spelling kept as an alias with a deprecation note).
- [ ] TUI login page: a full-screen sign-in page shown at startup whenever the TUI has no valid token (and after a 401 or sign-out). Fields: **Server URL** (defaults to the local service the launcher attaches to, for example `http://127.0.0.1:4317`, and accepts any reachable ArgueBot service such as `https://view.example.com`), **Email**, **Password** (masked), and **Tenant** (shown only after `tenant_required`, as a list of the returned tenants). A **Use a token instead** option replaces email and password with a masked bearer-token field. The page always shows the default credentials (`admin@arguebot`, `password`, token `arguebotadmin`). **Sign in** checks `/health` at the server URL first and reports an unreachable server, wrong credentials, a disabled user or tenant, and rate limiting as distinct inline messages, keeping the typed values.
- [ ] TUI connection memory: after a successful sign-in, store the server URL and session token per application home in a 0600 file (never the password). Next start reuses them if the session is still valid; otherwise the login page opens with the server URL filled in. `--server URL` and `ARGUEBOT_TOKEN` override the stored values. Sign out revokes the session and returns to the login page; Switch server opens the login page with the URL field focused.
- [ ] TUI workspace: the header shows the server, user and tenant. The Menu gets an **Account** section (change password, my credentials with the token shown once, my sessions, sign out, switch server) and, for tenant and system admins, **Administration** (users, tenants). Help gets an "Accounts, servers and sign-in" topic. All forms fit 60 by 20 terminals and both themes.
- [ ] Launch scripts (`go-tui`, `go-web`) keep working: `go-tui` starts or attaches to the local service and opens the login page with its URL filled in; `--server URL` skips the local service and targets the given server.

Acceptance: CLI surface script covers login, token, whoami, credential lifecycle, admin commands and secrets aliases; Terminal suite covers the TUI login page at 60 and 120 columns in both themes (server URL validation, unreachable server, wrong password, tenant choice, token sign-in, default credentials shown), reuse of a stored session, sign out and switch server, the account and administration menus, and the share flow, against a real service.

## Phase 7: API artifacts and documentation

- [ ] Regenerate `REST_API.md`, `assets/postman/openapi.json` and the Postman collection. The collection uses collection-level bearer auth with variable `{{token}}` defaulting to `arguebotadmin`, includes a Login request that stores the session token, and marks public requests as no-auth. Update `assets/postman/v2-acceptance.postman_collection.json` and `scripts/test-v2-postman.mjs`.
- [ ] `MCP_API.md`: state that the model tool server is invocation-scoped, reads only the run's conversation within the run's tenant, needs no bearer token, and never exposes identity, credential, secret or share operations.
- [ ] README: sign-in, default credentials and the instruction to change them, tenants and users, credentials, share links, remote access with `ARGUEBOT_ALLOWED_HOSTS` and TLS, and the Secrets rename.
- [ ] CHANGELOG under Unreleased: a 2.1.0 summary, then detailed entries; note the breaking change that every API call now needs a bearer token.
- [ ] `docs/IDENTITY.md` (rewrite), `docs/REQUIREMENTS.md` (deviations and manifest review), `docs/OPERATIONS.md` (seeding, password recovery by a system admin, audit retention), `docs/TUI.md`, `docs/MULTIPLE_AGENTS.md` (Secrets rename), `docs/PROMPTS.md` (tenant scope), `docker/README.md` and `DOCKERHUB_README.md` (published image, `ARGUEBOT_ALLOWED_HOSTS`, default credentials warning, TLS proxy example).

Acceptance: no stale references to an open workspace or to "Credentials" meaning provider secrets; no em dashes in new text; generated artifacts match the contract.

## Phase 8: Verification and release preparation

- [ ] Full console suite, xUnit and NUnit on .NET 8 and 10, PostgreSQL, CLI script, Postman, browser, component and Terminal suites, container acceptance with the bearer token, and hosted CI on the branch.
- [ ] Investigate the intermittent Chrome `net::ERR_TOO_MANY_RETRIES` on a navigation or reload while the page still has requests in flight (seen in the browser suite since v2.0, before sign-in existed; about 1 in 3 repeats of the sign-in reload step, never with a Chrome net log enabled and never from a Node client). Suspect Watson's handling of aborted TLS connections.
- [ ] The browser suite shares one address, so its deliberate wrong-password steps count toward the sign-in rate limit (10 per 5 minutes). Keep the count of such steps low or give the e2e service a test-only limit.
- [ ] Upgrade test: start 2.1.0 on a schema 29 home with existing conversations, sign in as the default admin, and confirm everything is present in the default tenant.
- [ ] Security review against "Security Best Practices" 1 to 19 (excluding RBAC items), recorded in `docs/IDENTITY.md`.
- [ ] Build and push `jchristn77/arguebot:v2.1.0` with `build-arguebot.sh v2.1.0` only when the owner asks.

## Phase checklist

- [x] Phase 0: branch, housekeeping and images (November 4, 2026: build with no warnings, Terminal suite 20 of 20, Compose resolves the named image and the build override, scripts pass `bash -n`)
- [x] Phase 1: schema and identity data
- [x] Phase 2: authentication pipeline
- [x] Phase 3: management API
- [x] Phase 4: dashboard
- [ ] Phase 5: public share links
- [ ] Phase 6: CLI and TUI
- [ ] Phase 7: API artifacts and documentation
- [ ] Phase 8: verification and release preparation
