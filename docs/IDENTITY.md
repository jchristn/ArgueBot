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

A **tenant** is a separate workspace. Conversations, runs, messages, agents, models, harnesses, endpoints, secrets, prompts, request history and audit records all carry a tenant ID, and every query filters by it. Another tenant's records read as 404, never 403, so their existence is not revealed.

Everyone in a tenant shares its data: any member can open, change and delete the tenant's conversations and configuration. The `user_id` on a conversation records who created it.

Users have two flags, giving three kinds of user:

| Kind | Flags | Can |
| --- | --- | --- |
| System administrator | `isAdmin` (implies `isTenantAdmin`) | Everything: all tenants, service settings, restart and shutdown, secret encryption keys, and any tenant's users, sessions and audit log (with `x-tenant-id`). |
| Tenant administrator | `isTenantAdmin` | Everything in their own tenant, including its users, their tokens and sessions, request history and the audit log. |
| User | neither | The tenant's conversations, debates, agents, secrets and prompts; their own profile, password, tokens and sessions. |

Only a system administrator can grant or remove system administration. Nobody can disable themselves or remove their own administrator access. Deleting a user keeps their conversations and agents in the tenant, credited to the administrator who deleted them (or the tenant's oldest other administrator when a system administrator acts from another tenant). Deleting a tenant needs its exact name and removes everything in it except its audit records.

## Signing in

`POST /v2.0/api/token` accepts `{ "email", "password", "tenantId" }` or the headers `x-email`, `x-password` and `x-tenant-id`. The password is checked against every account with that email (PBKDF2-SHA256, 600,000 iterations, constant-time comparison). If it matches in exactly one active tenant, that tenant is used. If it matches in several, the response is 409 `tenant_required` with only those tenants, so a wrong password learns nothing. Unknown emails take as long as wrong passwords.

The response holds a **session token** (`abs_...`): an AES-256-GCM encryption, with a random 96-bit nonce per token, of the session ID, user, tenant, issue and expiry times and a random token ID. The key is `secrets/session.key` in the application home (32 random bytes, owner-only, created on first start). Deleting it signs everyone out. The service stores only a SHA-256 hash of each token and checks the session record, user, tenant and backing credential on every request.

Sessions last 60 minutes. `POST /v2.0/api/token/refresh` issues a new token for the same session with a fresh lifetime, and the old token stops working. The dashboard and TUI refresh automatically while they run. `DELETE /v2.0/api/token` signs out. Changing your password signs out your other sessions; disabling a user or tenant ends all of its sessions at once.

**Credential tokens** (`abt_...`, 30 random bytes) are long-lived bearer tokens for scripts and tools, created under `/v2.0/api/me/credentials` (or by an administrator for another user). The token is returned once and stored as SHA-256. A credential can have an expiry and can be disabled or deleted. Its last use is recorded at most once a minute.

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

## Audit log

`audit_events` records sign-in successes and failures (never the password), session issue, refresh and revocation, first daily use of each credential, credential, user, tenant, password and share changes, every 401 and 403, and administrator bypasses. Each record has the request ID, principal, session or credential, method, path, source address, authentication and authorization results, reason and status code. Tenant administrators search their tenant's records at `GET /v2.0/api/audit`; system administrators see every tenant and unauthenticated requests. `DELETE /v2.0/api/audit?beforeUtc=...` removes old records and records that it did.

## Hosts and transport

The service accepts the host names `127.0.0.1` and `localhost`, plus any in `allowedHosts` in `arguebot.json` and the comma-separated `ARGUEBOT_ALLOWED_HOSTS`. Other `Host` headers get 403 `invalid_host`. A browser `Origin` must match the requested host (or be the Vite development server on port 5173).

Every request carries a password or token, so reach the service over HTTPS whenever it leaves the machine: enable TLS in the service settings or put it behind a TLS-terminating proxy that forwards the original `Host` (and `X-Forwarded-Proto: https`, which share links use to build `https://` URLs). The C# client, CLI and TUI refuse plain HTTP to a non-loopback server unless you pass `--allow-http` or tick the TUI's matching box.

## Clients

- **Dashboard:** keeps the session in `sessionStorage` only, sends it on every request and event stream, refreshes it five minutes before expiry, and returns to `/dashboard/login` on any 401.
- **TUI and CLI:** remember the server and token for the application home in `client-session.json` (owner-only, never the password). `ARGUEBOT_TOKEN` and `--token` override it and are never written to disk.
- **Model tools:** agents reach conversation tools through a per-invocation MCP bridge over a local pipe, scoped to the run's conversation and tenant. They never receive a bearer token. See [MCP_API.md](../MCP_API.md).

## What is not included

There are no roles, permissions or role assignments (RBAC), no separate accounts or administrator tables, no access-key and secret-key signing, and no single sign-on. The three kinds of user above are the whole access model. Historical role tables from earlier versions remain in the database, unused.
