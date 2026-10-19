# Shared runtime and compatibility

Accepted for implementation on October 19, 2026.

The C# service owns execution and persistence. Web, TUIKit, and scriptable console clients use its versioned API. CLI agents retain dedicated stdio adapters; PolyPrompt supplies REST completion adapters. Known state uses relational columns and child tables.

Legacy npm commands remain runnable until acceptance. Old /api/sessions routes remain on the legacy server during migration; the replacement uses /v1.0/api and does not promise transparent compatibility. Export reachable legacy sessions before stopping that server. No history is recoverable from already-lost memory-only sessions.

Local loopback still requires authentication. Initial enrollment uses a protected one-time pairing secret and sessions. Hosted CLI execution is disabled until isolation is verified. User data is never deleted on uninstall by default. Database migrations are forward-only with pre-upgrade backups; rollback restores a compatible binary and backup.
