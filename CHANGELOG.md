# Changelog

## Unreleased

### In progress

- Shared C# conversation runtime, durable application home and configurable runner architecture. The existing TypeScript entry points remain supported during migration. See IMPROVEMENTS.md for delivery gates and verified progress.

- React dashboard with persistent conversation navigation, participant configuration, streamed workspace updates, follow-up questions, request-history inspection, API Explorer, and settings/restart controls. Dark/light icon controls and hover help remain covered by browser checks.
- Shared SQLite/PostgreSQL driver structure, historical SQLite backup/upgrade checks, AES-GCM session protection and immediate revocation. Full hosted identity administration remains pending. Provider scope follows the user's SQLite/PostgreSQL-only instruction.
- TUIKit debate and moderator controls, search/paging, follow-ups, focus audit and theme regression tests; direct process capture now terminates promptly on malformed or oversized output.
- Generated REST/OpenAPI/Postman artifacts, cross-platform CI, and a provisioned local metrics/traces/logs stack with rotating native logs. These additions do not declare the replacement ready for cutover.
- Local console/TUI startup and attachment, owner-readable service metadata, concurrent pairing recovery, and authenticated graceful service stop/restart commands.
- Ordered migration tracking, portable checksummed database snapshots, pre-upgrade PostgreSQL snapshots, and offline transactional restore with corruption/rollback and PostgreSQL-to-SQLite tests. Complete recovery archives add optional authenticated encryption, explicit secret inclusion, dependency provenance, bounded service-owned jobs, authenticated download/verify/delete, and isolated SQLite-home restore. Direct complete-bundle restore into PostgreSQL remains pending.
