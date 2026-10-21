# Requirements traceability

October 20, 2026 scope override: remove the Grafana/Prometheus/Loki/Tempo stack, collector/export pipeline and associated dependencies and product controls. Keep ordinary local operational logs and built-in request history. Earlier external observability requirements are superseded.

October 20, 2026 scope override: user-facing backup, archive verification and restore features are removed from every interface and public API, including offline server commands. Earlier backup/restore product requirements are superseded. Existing data and internal database upgrade safeguards are preserved.

IMPROVEMENTS.md section 3 maps every normative document to implementation and evidence. requirements-manifest.json captures the reviewed source hashes. The default data home is ~/.arguebot, overridden by --home then ARGUEBOT_HOME. Tests must inject a disposable home.

The replacement remains under development until the plan gates pass. Existing npm and go-* entry points continue to run the legacy implementation during that period. New .NET commands are documented separately as they become runnable.

The strict backend requirements include Watson 7.1, PrettyId strings, typed contracts, structural tenant isolation, domain-specific database methods, and SQLite/PostgreSQL providers. The user explicitly removed MySQL and SQL Server from scope on October 19, 2026; this overrides the four-provider baseline in the normative backend document. Both supported drivers retain the full migration, backup/recovery and test obligations. Frontend work uses React/Vite and the full route inventory. Requirements conflicts and deviations must be recorded before implementation.

On October 20, 2026 the user removed all product identity functionality: one open workspace with no accounts, roles, permissions, authentication, pairing, sessions or security audit. This supersedes earlier opt-in authentication plans. See docs/IDENTITY.md.

On October 20, 2026 the user required REST-connected models to execute local tools instead of remaining completion-only. The dedicated executor shares the service local-execution switch with CLI harnesses and records call/result history; details and verification scope are in docs/EXECUTION.md.

October 20, 2026 product scope override: remove user accounts, roles, permissions, security audit, login/pairing and identity-dependent controls from every product surface and public API. ArgueBot is a single open workspace. Existing internal database ownership keys and historical tables remain for non-destructive compatibility, with no exposed identity management or authorization dependency for local work.
