# Requirements traceability

October 20, 2026 scope override: remove the Grafana/Prometheus/Loki/Tempo stack, collector/export pipeline and associated dependencies and product controls. Keep ordinary local operational logs and built-in request history. Earlier external observability requirements are superseded.

October 20, 2026 scope override: user-facing backup, archive verification and restore features are removed from every interface and public API, including offline server commands. Earlier backup/restore product requirements are superseded. Existing data and internal database upgrade safeguards are preserved.

[requirements-manifest.json](requirements-manifest.json) captures the reviewed normative source hashes. The [archived plan's traceability matrix](../archive/IMPROVEMENTS.md#3-requirements-traceability-and-scope) preserves the original document mapping as historical context; it does not override the current scope corrections here. The default data home is ~/.arguebot, overridden by --home then ARGUEBOT_HOME. Tests must inject a disposable home.

The supported application consists of the C# service and TUI plus the React dashboard. Source launchers are go-tui and go-web; npm is used only beneath dashboard. Remaining acceptance work does not require retaining retired interfaces.

The strict backend requirements include Watson 7.1, PrettyId strings, typed contracts, structural tenant isolation, domain-specific database methods, and SQLite/PostgreSQL providers. The user explicitly removed MySQL and SQL Server from scope on October 19, 2026; this overrides the four-provider baseline in the normative backend document. Both supported drivers retain the full migration, backup/recovery and test obligations. Frontend work uses React/Vite and the full route inventory. Requirements conflicts and deviations must be recorded before implementation.

On October 20, 2026 the user removed all product identity functionality: one open workspace with no accounts, roles, permissions, authentication, pairing, sessions or security audit. The November 4, 2026 identity scope below supersedes this for 2.1.

On October 20, 2026 the user required REST-connected models to execute local tools instead of remaining completion-only. The dedicated executor shares the service local-execution switch with CLI harnesses and records call/result history; details and verification scope are in docs/EXECUTION.md.

October 20, 2026 product scope override (superseded for 2.1 on November 4, 2026): remove user accounts, roles, permissions, security audit, login/pairing and identity-dependent controls from every product surface and public API.

## November 9, 2026 v2.3 questions scope

The user asked for agents to be able to stop mid-turn and ask the user structured questions (open text, single choice, multi-select, confirmation) on every product surface, with answers given in modal dialogs rather than inline forms. [PROMPTING_THE_USER_PLAN.md](../PROMPTING_THE_USER_PLAN.md) records the decisions and work; [adr/0007-user-questions.md](adr/0007-user-questions.md) records the hold-then-park design. The user's decisions on November 9, 2026:

- Target version 2.3.0 on `feature/v2.3.0`.
- Every seat may ask by default (`userQuestions: all`), within per-turn and per-run limits. New debates start with an intake step (`intake: ask_if_needed`); upgraded debates keep intake off.
- No tray app. Desktop notifications come from the dashboard through browser notifications.
- `request_clarification`, the clarifications route, `clarificationId`/`skipClarification` on interjections and `debate_clarifications` are removed in 2.3.0 after their rows migrate into question requests. This is a breaking change.
- Touchstone moves to 1.0.0 before the feature work (see below).
- Shared transcripts always show questions and answers.

All normative documents were re-read on November 9, 2026. Every hash matched the manifest except BACKEND_TEST_ARCHITECTURE.md. Its reviewed version is requirements commit `6a8d917` (`c081fa99...`); the only later change is commit `11a3982` (`f653dca3...`), which updates the document for Touchstone 1.0.0: separate Core, Cli and per-framework adapter packages (xUnit v3, xUnit v2, NUnit, MSTest), the suites class as an `ITestSuiteProvider` with fresh descriptors, `skipReason` instead of a skip flag, tags, immutable descriptors, `ConsoleRunner.RunMainAsync` with Touchstone's own options, versioned JSON results, defined exit codes, one run per framework in CI and every host in CI. ArgueBot now follows it: `Test.Xunit` became `Test.Xunit3` on `Touchstone.Xunit3Adapter` with xUnit v3 (VSTest mode, so `dotnet test` and TRX logging keep working), `Test.Nunit` uses the 1.0 NUnit adapter, both hosts run a run-all test and one test per case, and CI runs `Test.Automated` on .NET 8 and .NET 10. No MSTest or xUnit v2 host exists because the project uses neither framework. The manifest now records the new hash.

Deviations recorded for this scope, each existing practice or forced by an earlier decision:

- **Live updates use server-sent events**, the product's existing channel for conversation events, rather than the WebSocket endpoint described in BACKEND_ARCHITECTURE.md. Clients fall back to polling the question summary.
- **IDs use the existing `Ids.New(prefix)` helper** (`qrq_`, `qst_`, `qop_`, `qan_`) rather than a central `Constants.cs` and `IdGenerator`.
- **Routes stay under `/v2.0/api/...`**, the established prefix.
- **Access classes replace RBAC operation tuples** (November 4, 2026 scope): reading questions is Member read; answering, skipping, deferring and deleting is Member write.
- **The C# SDK stays at `src/ArgueBot.Client`** without a separate `sdk/csharp` harness (REPOSITORY_REQUIREMENTS.md #7). Client checks in Test.Shared run in every CI host instead.

## November 5, 2026 v2.2 resources scope

The user asked for a top-level Resource entity in 2.2.0: named markdown documents that agents can list, read, create and edit through tools during a debate, so prompts can point agents at context to pull in when needed and agents can build their own. The decisions are recorded in [RESOURCES_PLAN.md](../archive/RESOURCES_PLAN.md) and summarized here:

- Resources are database rows in the tenant's shared library, like prompts and agents. The service remains the sole writer, and SQLite and PostgreSQL both store them (schema 33).
- Each resource has a unique, case-insensitive name within the tenant, a markdown body up to 256 KiB, a title, a one-line description, optional tags, a locked flag and a full revision history. Updates use optimistic concurrency on the revision.
- Each debate sets resource access to `off`, `read` (the default) or `read_write`, and can narrow which resources it sees by name or tag. Agents never change locked resources. Agent-created resources go straight into the shared library, marked as agent-authored, with history and revert.
- The agent tools are plain conversation tools (`list_resources`, `search_resources`, `read_resource`, `create_resource`, `update_resource`, `edit_resource`, `append_resource`), not the MCP resources primitive, so they reach Claude Code, Codex, Mux, Gemini, generic CLIs and REST models the same way.
- Prompts get an opt-in `{resources}` placeholder.

BACKEND_ARCHITECTURE.md, BACKEND_TEST_ARCHITECTURE.md, FRONTEND_ARCHITECTURE.md, DASHBOARD_STYLE_AND_USABILITY.md, I18N.md and CODE_STYLE.md were re-checked on November 5, 2026; their hashes are unchanged from the manifest. Resources follow the existing rules (PrettyId `res_` and `rsr_` IDs, typed contracts, tenant-scoped domain methods, both database providers, audited changes, localized dashboard text). No new deviation is introduced beyond those already recorded for 2.1: there is no RBAC, so any member of a tenant can manage its resources.

The implementation (November 5, 2026) follows these decisions. Its small deviations from the plan's wording (a leading letter-or-digit name rule, one request type for create and update, a larger MCP frame limit, and upgraded debates keeping access off) are listed in the plan's execution record. The user guide is [RESOURCES.md](RESOURCES.md).

## November 4, 2026 v2.1 identity scope

The user asked for users, bearer-token credentials, a login page, API authentication and multitenancy in 2.1.0, with a default tenant and the default administrator `admin@arguebot` (password `password`, token `arguebotadmin`) created at startup, the login screens always showing the default credentials, and no RBAC. Users are system administrators, tenant administrators or ordinary users. Data is shared within a tenant. Provider credentials are renamed secrets. [IDENTITY.md](IDENTITY.md) describes the result; [archive/USER_AUTH_PLAN.md](../archive/USER_AUTH_PLAN.md) records the work and evidence.

AUTHENTICATION.md was re-read for this scope on November 4, 2026; its hash is unchanged from the manifest. Deviations, each chosen by the user or forced by the no-RBAC decision:

- **No RBAC.** The role, permission, role-map, assignment and credential-scope tables and the effective-permission resolver are not used. The bypass rules (`isadmin`, `istenantadmin`) are the whole model, with an ordinary user getting full data access inside the tenant and no administration. Each route has a fixed access class (Public, Member, TenantAdmin, SystemAdmin) instead of a permission tuple; 403 bodies carry `requiredPermission` set to that class.
- **No accounts or administrators tables.** System administrators are users with `isadmin`.
- **Credentials are bearer tokens only** (`DirectHeader`): no access-key and secret-key pairs or request signing. Tokens are verified by SHA-256 and also kept encrypted under a separate key so their owner or a tenant administrator can reveal them (audited), which the requirement allows as "encrypted secret material"; credential grants equal the owning user's access rather than a narrower scope.
- **Session tokens use AES-256-GCM** with a random nonce per token instead of AES-CBC plus a separate MAC; GCM authenticates the ciphertext.
- **Routes live under `/v2.0/api/token`** rather than `/v1.0/token`, matching the product's API version.
- **Tenant header** is `x-tenant-id` (with `x-tenant-guid` accepted at sign-in), matching the product's string IDs.
- **Default secrets are documented**: the default password and token are intentionally known and shown on every sign-in page, as the user requested. The default records are protected so an operator cannot lock themselves out; operators are told to change the password and stop using the default token.
- **Audit records survive tenant deletion** so a tenant's security history remains reviewable; retention deletion removes them.

## November 3, 2026 v2 reconciliation

The three changed normative sources were compared against the exact historical blobs matching the prior manifest hashes: BACKEND_ARCHITECTURE at `aab234fc50c1cf9723ff26a7dafb5679d90ce4c2`, BACKEND_TEST_ARCHITECTURE at `aacbccfd7294ac1d94e38ac73aa77d318529bb4f`, and REPOSITORY_REQUIREMENTS at `8f971b2fd5b1a2504f470afc4633a7771bf74c6f` in the requirements repository. The complete changes add all-branch CI, every test adapter/provider, cross-platform execution and an explicit AOT decision. Other source hashes are unchanged. The manifest now records this review.

ArgueBot retains shared Touchstone descriptors, console/xUnit/NUnit adapters, .NET 8/.NET 10 library coverage and the Linux/macOS/Windows CI matrix. PostgreSQL integration runs in the Linux job; SQLite runs on every OS. New native MCP, configuration, memory and debate descriptors join the shared inventory. The dashboard job includes its provider fixture, and integration adds the CLI and isolated Postman workflow. CI also builds and probes the application container. Workflow configuration is not evidence that hosted jobs have run.

AOT support is explicitly **Not supported** in README: runtime-selected reflection serialization has no generated metadata and is not trim-safe. No AOT compatibility is advertised, and no Test.Aot exclusion is hidden inside a passing native test job.

Scope overrides for SQLite/PostgreSQL, no external telemetry and no public backup/restore workflows remain in force. The container update helper builds a local image because no v2 image has been published; it preserves volumes. Docker's actual two-failure health semantics are documented without promising a two-success transition that Docker does not implement. Imported provider secrets are unrelated to sign-in.
