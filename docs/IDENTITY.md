# Sign-in, tenants and access

ArgueBot 2.1 requires a signed-in user for every interface and API call except the sign-in page, `/health`, `GET /v2.0/api/login-info`, `POST /v2.0/api/token`, public share links and the OpenAPI document. This page describes how identity works and how it maps to AUTHENTICATION.md in the shared requirements (`~/Code/Agents/requirements`). Deviations are listed in [REQUIREMENTS.md](REQUIREMENTS.md).

## Default records

Every startup makes sure these exist, creating whatever is missing and never resetting a value you changed:

| Record | Value | Notes |
| --- | --- | --- |
| Tenant | `ten_local`, named **Default tenant** | Holds all data created before 2.1. Protected: cannot be deleted or disabled. |
| User | `usr_local`, **admin@arguebot**, password **password** | System administrator and tenant administrator. Protected: cannot be deleted, disabled or lose administrator access. The password is set only when the user has none. |
| Credential | `cred_default_admin`, **Default admin token**, token **arguebotadmin** | Bearer token of the default administrator. Protected: cannot be disabled or deleted. |

The sign-in pages always show these defaults. Change the default password after the first sign-in, and give other people their own accounts and tokens. To stop using the default token, remove anything that uses it; it cannot be disabled because it is the recovery path if every other administrator loses access.

## Tenants and users

A **tenant** is a separate workspace. Conversations, runs, messages, agents, models, endpoints, secrets, prompts, [resources](RESOURCES.md), request history and audit records all carry a tenant ID, and every query filters by it. Another tenant's records read as 404, never 403, so their existence is not revealed.

Everyone in a tenant shares its data: any member can open, change and delete the tenant's conversations and configuration. The `user_id` on a conversation records who created it.

Users have two flags, giving three kinds of user:

| Kind | Flags | Can |
| --- | --- | --- |
| System administrator | `isAdmin` (implies `isTenantAdmin`) | Everything: all tenants, service settings, restart and shutdown, secret encryption keys, and any tenant's users, sessions and audit log (with `x-tenant-id`). |
| Tenant administrator | `isTenantAdmin` | Everything in their own tenant, including its users, their tokens and sessions, request history and the audit log. |
| User | neither | The tenant's conversations, debates, agents, secrets, prompts and resources; their own profile, password, tokens and sessions. |

Only a system administrator can grant or remove system administration. Nobody can disable themselves or remove their own administrator access. Deleting a user keeps their conversations and agents in the tenant, credited to the administrator who deleted them (or the tenant's oldest other administrator when a system administrator acts from another tenant). Deleting a tenant needs its exact name and removes everything in it except its audit records.

## Signing in

`POST /v2.0/api/token` accepts `{ "email", "password", "tenantId" }` or the headers `x-email`, `x-password` and `x-tenant-id`. The password is checked against every account with that email (PBKDF2-SHA256, 600,000 iterations, constant-time comparison). If it matches in exactly one active tenant, that tenant is used. If it matches in several, the response is 409 `tenant_required` with only those tenants, so a wrong password learns nothing. Unknown emails take as long as wrong passwords.

The response holds a **session token** (`abs_...`): an AES-256-GCM encryption, with a random 96-bit nonce per token, of the session ID, user, tenant, issue and expiry times and a random token ID. The key is `secrets/session.key` in the application home (32 random bytes, owner-only, created on first start). Deleting it signs everyone out. The service stores only a SHA-256 hash of each token and checks the session record, user, tenant and backing credential on every request.

Sessions last 60 minutes. `POST /v2.0/api/token/refresh` issues a new token for the same session with a fresh lifetime, and the old token stops working. The dashboard and TUI refresh automatically while they run. `DELETE /v2.0/api/token` signs out. Changing your password signs out your other sessions; disabling a user or tenant ends all of its sessions at once.

**Credential tokens** (`abt_...`, 30 random bytes) are long-lived bearer tokens for scripts and tools, created under `/v2.0/api/me/credentials` (or by an administrator for another user). Authentication compares only its SHA-256 hash. The service also keeps a copy encrypted with `secrets/token.key` (AES-256-GCM), so the owner or a tenant administrator can reveal it later; every reveal is audited as `CredentialRevealed`. A credential can have an expiry and can be disabled or deleted. Its last use is recorded at most once a minute.

## Revealing sensitive values

Some stored values can be shown again, always to someone entitled to them and always with an audit record:

| Value | How to reveal | Who | Audit event |
| --- | --- | --- | --- |
| Bearer-token credential | `POST /v2.0/api/me/credentials/{id}/reveal`, or `POST /v2.0/api/users/{userId}/credentials/{id}/reveal`; dashboard Account (or Users > Tokens and sessions) > Actions > Reveal; TUI Menu > Your bearer tokens; CLI `credentials reveal ID [--user ID]` | The owner; tenant administrators for users in their tenant | `CredentialRevealed` |
| Public share link | `POST /v2.0/api/conversations/{id}/shares/{shareId}/reveal`; Share dialog > Actions > Show link; TUI share menu; CLI `shares reveal` | Members of the tenant, while the link is active | `ShareRevealed` |
| Provider secret | `POST /v2.0/api/configuration/secrets/{id}/reveal`; Participants > Secrets > Actions > Reveal; TUI Secrets > Reveal value; CLI `secrets reveal ID` | Members of the tenant | Request history (body never stored) |

Tokens created before reveal support (before schema 31) cannot be revealed and return 409 `not_revealable`, except the default token, whose value is public. Deleting `secrets/token.key` makes existing tokens unrevealable but leaves them working. Passwords and session tokens are never stored in a recoverable form and cannot be revealed. Request history never stores reveal responses.

## Sending a token

Send exactly one token as `Authorization: Bearer <token>` or `x-token: <token>`. Both may be sent only if they are the same token. Any other `Authorization` scheme is refused, and `x-password` on any route other than sign-in is refused.

| Status | Codes |
| --- | --- |
| 401 | `authentication_required`, `invalid_token`, `session_expired`, `session_revoked`, `credential_inactive`, `credential_expired`, `user_inactive`, `tenant_inactive`, `conflicting_credentials`, `unsupported_scheme`, `password_not_accepted`; at sign-in `invalid_credentials`, `account_inactive` |
| 403 | `forbidden` (the route needs a higher access class) or `tenant_mismatch` (`x-tenant-id` differs from the token's tenant), with an `AuthorizationError` body naming the reason, `requiredPermission` and `requestType` |
| 409 | `tenant_required` at sign-in |
| 429 | `rate_limited`, with `Retry-After`, after 10 failed sign-ins from one address or for one email within 5 minutes |

## Access classes

Every authenticated route has one access class, checked before its handler runs (`RouteAccess` in `ArgueBot.Core/Services`). The OpenAPI document lists it for each operation as `x-arguebot-access`, and a test confirms the two agree.

| Class | Routes |
| --- | --- |
| Public | `/health`, dashboard files and `/share/*` pages, `GET /openapi.json`, `GET /v2.0/api/login-info`, `POST /v2.0/api/token`, `GET /v2.0/api/shares/{token}` |
| Member | Conversations, runs, messages, events, debates, configuration (except encryption keys), prompts, capabilities, `/token`, `/me/*`, conversation shares |
| TenantAdmin | `/users`, `/sessions`, `/audit`, request history |
| SystemAdmin | `/tenants`, settings, restart and shutdown, `/configuration/secret-keys` |

A system administrator may send `x-tenant-id` on TenantAdmin routes to act in another tenant. That bypass is recorded as `AuthorizationBypass`.

## Questions

Any member of a tenant can answer or skip a question in any conversation of that tenant, as with interjections; the service records who resolved it. The Questions page and `GET /v2.0/api/questions?scope=mine` list conversations you created; `scope=tenant` lists the whole tenant. Another tenant's question returns 404.

## Audit log

`audit_events` records sign-in successes and failures (never the password), session issue, refresh and revocation, first daily use of each credential, credential, user, tenant, password and share changes, answered and skipped questions (IDs only, never the answer text), every 401 and 403, and administrator bypasses. Each record has the request ID, principal, session or credential, method, path, source address, authentication and authorization results, reason and status code. Tenant administrators search their tenant's records at `GET /v2.0/api/audit`; system administrators see every tenant and unauthenticated requests. `DELETE /v2.0/api/audit?beforeUtc=...` removes old records and records that it did.

## Hosts and transport

The service accepts the host names `127.0.0.1` and `localhost`, plus any in `allowedHosts` in `arguebot.json` and the comma-separated `ARGUEBOT_ALLOWED_HOSTS`. Other `Host` headers get 403 `invalid_host`. The entry `*` accepts any host name, for services reached by names you can't list in advance; the service logs a warning at startup. The host check mainly defends against DNS rebinding, where a hostile web page makes a visitor's browser talk to your service. With `*`, sign-in is the remaining defense, and the default administrator password is public, so change it before using `*`. The listen address is separate: `listenAddress` stays `127.0.0.1` unless you set `0.0.0.0`. A browser `Origin` must match the requested host (or be the Vite development server on port 5173).

Every request carries a password or token, so reach the service over HTTPS whenever it leaves the machine: enable TLS in the service settings or put it behind a TLS-terminating proxy that forwards the original `Host` (and `X-Forwarded-Proto: https`, which share links use to build `https://` URLs). A proxy that rewrites `Host` can send the public name in `X-Forwarded-Host`; share links use it when present. The C# client, CLI and TUI refuse plain HTTP to a non-loopback server unless you pass `--allow-http` or tick the TUI's matching box.

## Clients

- **Dashboard:** keeps the session in `sessionStorage` only, sends it on every request and event stream, refreshes it five minutes before expiry, and returns to `/dashboard/login` on any 401.
- **TUI and CLI:** remember the server and token for the application home in `client-session.json` (owner-only, never the password). `ARGUEBOT_TOKEN` and `--token` override it and are never written to disk.
- **Model tools:** agents reach conversation tools through a per-invocation MCP bridge over a local pipe, scoped to the run's conversation and tenant. They never receive a bearer token. See [MCP_API.md](../MCP_API.md).

## What is not included

There are no roles, permissions or role assignments (RBAC), no separate accounts or administrator tables, no access-key and secret-key signing, and no single sign-on. The three kinds of user above are the whole access model. Historical role tables from earlier versions remain in the database, unused.

## Security review (November 4, 2026)

Reviewed against "Security Best Practices" in AUTHENTICATION.md.

| # | Practice | Status |
| --- | --- | --- |
| 1 | Password and secret handling | Met. Passwords are PBKDF2-SHA256 (600,000 iterations, per-user salt); session tokens are stored only as SHA-256; credential and share tokens are verified by SHA-256 and kept encrypted (AES-256-GCM, separate key) only for audited reveal; comparisons use `CryptographicOperations.FixedTimeEquals`. Request history keeps no bodies for sign-in, token, credential, secret, share, user or password routes, and redacts password and token fields and headers elsewhere. |
| 2 | Token encryption | Met with AES-256-GCM and a random 96-bit nonce per token (authenticated encryption). |
| 3 | Key redaction | Met. Credential and share lists show only the last four characters. Full tokens appear only in the create response and in explicit, audited reveal requests. |
| 4 | Tenant isolation | Met. Every query filters by the tenant from the token; requests without a resolvable tenant are 401. Cross-tenant reads and writes of conversations, runs, messages, secrets, endpoints, prompts and shares return 404 (real-service test). |
| 5 | Active flags | Met for users, tenants, credentials and sessions on every request. Roles and permissions are not used. |
| 6 | Protected flags | Met. The default tenant, administrator and token cannot be deleted or disabled, and the administrator cannot lose administrator access. |
| 7, 8 | Explicit and implicit deny | Not applicable without RBAC. Every route has a fixed access class; anything not permitted by it is denied. |
| 9 | Admin bypass | Met. The three user types and the `x-tenant-id` bypass are documented above; cross-tenant administrator actions are audited as `AuthorizationBypass`, and identity changes record the acting administrator. |
| 10 | Request tracing | Met. Audit records and request history carry the request ID; request history also carries the trace ID. |
| 11 | Least-privilege credentials | Partly met (deviation). Users can create a separate credential per integration with an expiry, but a credential has its user's full access because there are no scopes. |
| 12 | Admin endpoint hardening | Met. Tenant-admin and system-admin routes are refused before their handlers run, with no member fallback; the real-service test confirms every such route refuses a member with 403. |
| 13 | Audit denials | Met. Every 401 and 403 from the gate is persisted. |
| 14 | Constant-time comparisons | Met for passwords and token hashes. |
| 15 | Rate limiting | Met for sign-in (10 failures per address or email in 5 minutes) and public share reads (120 per address per minute). Limits are per process and reset on restart. |
| 16 | Client-side checks are UI-only | Met. The dashboard and TUI hide administration from members, and the server enforces every access class regardless. |
| 17 | Session revocation | Met. Sign-out, password change, user or tenant disablement and credential disablement stop the affected tokens immediately (tested). |
| 18 | Replay protection for signed requests | Not applicable: there are no signed credential requests. |
| 19 | Immutable seeded roles | Not applicable: there are no roles. |

Known limits: the default password and token are public by design until changed; anyone who can reach the port can try them, so operators must change the password before exposing the service. Rate-limit state is in memory. Members of a tenant can reveal its stored secrets, as in 2.0.
