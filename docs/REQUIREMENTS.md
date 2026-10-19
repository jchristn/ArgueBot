# Requirements traceability

IMPROVEMENTS.md section 3 maps every normative document to implementation and evidence. requirements-manifest.json captures the reviewed source hashes. The default data home is ~/.arguebot, overridden by --home then ARGUEBOT_HOME. Tests must inject a disposable home.

The replacement remains under development until the plan gates pass. Existing npm and go-* entry points continue to run the legacy implementation during that period. New .NET commands are documented separately as they become runnable.

The strict backend requirements include Watson 7.1, PrettyId strings, typed contracts, structural tenant isolation, domain-specific database methods, and Sqlite/Mysql/Postgresql/SqlServer providers. SQLite-first progress does not waive the other providers. Frontend work uses React/Vite and the full route inventory. Requirements conflicts and deviations must be recorded before implementation.
