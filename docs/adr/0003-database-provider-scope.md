# SQLite and PostgreSQL provider scope

Status: accepted by direct user instruction on October 19, 2026.

The user narrowed the implementation to SQLite and PostgreSQL: "You only need sqlite and postgres. no need for mysql and sql server."

This instruction overrides the four-provider requirement in the reviewed `BACKEND_ARCHITECTURE.md`. The requirements source and reviewed hash remain unchanged; ArgueBot records this explicit product-scope exception in its plan and traceability document. SQLite remains the embedded default. PostgreSQL is the supported server provider. Both retain the migration, structural tenant isolation, domain-method, backup/restore and shared-test obligations.

Remove the excluded drivers and SQL dialects, NuGet dependencies, test descriptors, Compose fixtures, UI choices and API enum entries. No SQL Server license acceptance is needed. Keep PostgreSQL's existing numeric enum value 2 so preexisting numeric service settings continue to identify the same provider. New settings serialize the provider name.

Portable database snapshots are a declared technical interchange format, not a generic JSON domain store. Runtime platform state stays relational with known columns and domain methods. Snapshot readers accept only known tables/columns, parameterize values, verify ordering/counts/checksum before restore, and restore in a database transaction. This format carries database contents only; settings and token encryption keys require the separate recovery-bundle workflow.
