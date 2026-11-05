# Requirements traceability

October 20, 2026 scope override: remove the Grafana/Prometheus/Loki/Tempo stack, collector/export pipeline and associated dependencies and product controls. Keep ordinary local operational logs and built-in request history. Earlier external observability requirements are superseded.

October 20, 2026 scope override: user-facing backup, archive verification and restore features are removed from every interface and public API, including offline server commands. Earlier backup/restore product requirements are superseded. Existing data and internal database upgrade safeguards are preserved.

[requirements-manifest.json](requirements-manifest.json) captures the reviewed normative source hashes. The [archived plan's traceability matrix](../archive/IMPROVEMENTS.md#3-requirements-traceability-and-scope) preserves the original document mapping as historical context; it does not override the current scope corrections here. The default data home is ~/.arguebot, overridden by --home then ARGUEBOT_HOME. Tests must inject a disposable home.

The supported application consists of the C# service and TUI plus the React dashboard. Source launchers are go-tui and go-web; npm is used only beneath dashboard. Remaining acceptance work does not require retaining retired interfaces.

The strict backend requirements include Watson 7.1, PrettyId strings, typed contracts, structural tenant isolation, domain-specific database methods, and SQLite/PostgreSQL providers. The user explicitly removed MySQL and SQL Server from scope on October 19, 2026; this overrides the four-provider baseline in the normative backend document. Both supported drivers retain the full migration, backup/recovery and test obligations. Frontend work uses React/Vite and the full route inventory. Requirements conflicts and deviations must be recorded before implementation.

On October 20, 2026 the user removed all product identity functionality: one open workspace with no accounts, roles, permissions, authentication, pairing, sessions or security audit. The November 4, 2026 identity scope below supersedes this for 2.1.

On October 20, 2026 the user required REST-connected models to execute local tools instead of remaining completion-only. The dedicated executor shares the service local-execution switch with CLI harnesses and records call/result history; details and verification scope are in docs/EXECUTION.md.

October 20, 2026 product scope override (superseded for 2.1 on November 4, 2026): remove user accounts, roles, permissions, security audit, login/pairing and identity-dependent controls from every product surface and public API.

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
