# Local service operations

The replacement service binds to 127.0.0.1 and owns its application home exclusively. Start it with `dotnet run --project src/ArgueBot.Server -c Release -- --home .run/home`. Production hosted access, complete identity administration, distribution packages, and release acceptance remain unfinished; see IMPROVEMENTS.md.

Local console/TUI clients automatically attach through owner-readable `runtime/service.json` metadata or start a sibling service binary for the selected home. Source builds can locate the Release service output in the same checkout. `--server` always attaches explicitly; `--no-start` disables automatic startup. Background startup detaches the Unix session or creates a windowless Windows process, redirects startup messages to `logs/startup.log`, and leaves the service running when a client exits. Concurrent startup still relies on the exclusive home lock rather than PID metadata. The console's `service stop` uses the authenticated graceful shutdown endpoint; process supervisors may restart a stopped container according to their own policy.

Settings live in `arguebot.json` beneath the selected home. Configuration precedence is `--home`, `ARGUEBOT_HOME`, then the operating system profile's `.arguebot` directory. `--port` overrides the configured listener port for the process. The authenticated Settings page displays the effective port and whether the saved configuration requires restart. Save uses a revision precondition and atomic file replacement. Restart detaches HTTP clients, drains workers, releases ownership, and reloads configuration. In containers the process exits for a supervisor instead of launching a second copy. Keep an explicit port override in mind when testing a saved listener change.

SQLite is the default. Startup refuses a future schema instead of downgrading it, and a supported historical SQLite upgrade creates a backup first. PostgreSQL creates a protected logical snapshot before a supported upgrade. Portable database snapshots stream known tables, preserve Unicode and null/integer values, and verify format, ordering, row counts and SHA-256 before transactional restore. Complete recovery archives include settings, optional encrypted key material and portable relational snapshots; see [Recovery](RECOVERY.md). Do not point development fixtures at an existing installation.

The local pairing code is owner-readable and rotates after use. Sessions expire after one hour and can be revoked immediately. AES-GCM session encryption keys live in `keys`; the database stores session digests. A filesystem backup of only the database omits settings and keys. Do not delete retained keys while sessions issued under them remain valid. Recovery bundles restore into separate homes and revoke recovered sessions. Automated key-retention pruning remains pending.

Request history captures redacted JSON body prefixes, structured redacted headers, method, path, status, duration, sizes, correlation and trace IDs. Default capture limit is 32768 bytes per body and retention is thirty days. The inspector requires an operator or administrator role. Retention runs after traffic, at most hourly, rather than on an idle timer. Non-JSON bodies and SSE bodies are omitted. Query strings are excluded. The normal list loads metadata; body and header detail requires a separate authorized request. History remains diagnostic data and can contain user conversation content.

## Observability

Run `docker compose up -d --build` from the repository root to start the isolated local observability stack. It provisions the collector, Prometheus, Tempo, Loki and Grafana with loopback-only published ports and persistent Compose volumes. This Compose file currently hosts observability; it does not package the ArgueBot application. It leaves other Compose projects untouched.

| Component | Local address | Purpose |
| --- | --- | --- |
| Collector | `http://127.0.0.1:4327` | OTLP gRPC receiver |
| Prometheus | `http://127.0.0.1:9097` | Metric queries |
| Tempo | `http://127.0.0.1:3207` | Trace search API |
| Loki | `http://127.0.0.1:3107` | Log search API |
| Grafana | `http://127.0.0.1:3007` | Provisioned HTTP and execution dashboards, traces and logs |

Default local Grafana credentials are `admin` / `arguebot-local-observability`. They are development defaults, not a credential for any remote service. Change the Compose environment before using a different trust boundary. These tools listen without application authorization on the Compose network; no published port binds to a public interface.

In the disposable service home's `arguebot.json`, enable `Telemetry.ExportEnabled` and leave `Telemetry.Endpoint` as `http://127.0.0.1:4327`. Restart the service. Watson HTTP spans and metrics, ArgueBot storage/run/runner instrumentation, PolyPrompt instrumentation, and structured service/execution logs then flow through Radiant to the collector. Metrics export every fifteen seconds; wait for an export and scrape before checking an empty dashboard. Local native operation has export disabled by default. Telemetry is emitted regardless of export configuration.

Application metric names include `arguebot.storage.writes`, `arguebot.storage.write.duration`, `arguebot.runner.generations`, `arguebot.runner.duration`, `arguebot.runner.first_text.duration`, and `arguebot.runs.started`. Durations use seconds and explicit latency buckets. Metric dimensions contain bounded provider, runner, mode and outcome values. Conversation IDs, run IDs, prompts, credentials and endpoint addresses do not belong in metric labels. Operational log messages include run IDs and safe outcomes, and active trace/span IDs supply correlation. Prometheus normalizes instrument dots to underscores and adds unit/type suffixes.

Local JSONL operational logs live in `logs/operations.jsonl`, with four rotated files and a 5 MiB rotation threshold. File logs are written at information level and above. Application directories and sensitive files use owner-only Unix modes or a protected Windows DACL for the current user. Capture failures use a separate best-effort type-only log. Loki retains seven days; Tempo retains one day. Compose metric storage uses Prometheus defaults. Disk quota policies, configurable file-log retention, complete administrative log browsing and protected audit coverage remain open work.

`docker compose down` stops this stack and preserves its named volumes. Do not add `-v` unless intentionally discarding its historical telemetry. Configuration files are mounted read-only; restart the affected component after editing them. The image wrappers include cURL so healthchecks use the required five-second interval, timeout no greater than five seconds, and two consecutive failures. Docker does not implement a two-success recovery hysteresis.


## Offline database snapshots

Stop the service before exporting or restoring with the maintenance host; the same exclusive home lock protects maintenance and normal operation. A restore requires an explicit destination home and creates a snapshot of its previous database first. It validates the input before opening the target database, stages a protected copy, and restores in one transaction. A checksum or database constraint failure preserves the target's existing data. Normal startup reconciliation pauses uncertain generations after restoration. Snapshots can transfer PostgreSQL data into SQLite; the integration suite verifies that path.

```sh
dotnet run --project src/ArgueBot.Server -c Release -- --home .run/home --export-snapshot .run/database.arguedb.jsonl
dotnet run --project src/ArgueBot.Server -c Release -- --verify-snapshot .run/database.arguedb.jsonl
dotnet run --project src/ArgueBot.Server -c Release -- --home .run/restored --restore-snapshot .run/database.arguedb.jsonl
```

Database snapshots contain private conversation and diagnostic data. They use owner-only files on Unix, but they are not encrypted recovery bundles and they do not contain settings or token-encryption keys. SHA-256 detects accidental corruption and truncation; it is not a signature proving an archive's origin. Keep snapshots within the same protected trust boundary. The format bounds each JSONL record and streams rows rather than loading the entire database into memory. Restoring an old schema uses the supported sequence-column mapping and the normal ordered upgrade process.

The service owns both its home and its selected database. SQLite holds an owner-only sibling `*.arguebot-owner` lock file, so different homes cannot accidentally run the same database. Leave the file in place; its presence alone does not indicate a live owner. PostgreSQL keeps a nonpooled advisory-lock session and uses that same connection for every write. A second service is refused. Losing the writer session cancels the host ownership signal, stops workers and exits with failure; it never transparently reconnects beside a successor. Use a dedicated PostgreSQL database for each ArgueBot instance. Generation attempts add atomic checkpoints and explicit uncertain-work recovery. Hosted process isolation remains a separate unfinished gate.

Windows background service launch uses a detached native process with an explicit inherited-handle whitelist containing only the null device. Capturing clients must not lend their stdout/stderr pipes to the service. The implementation follows the documented [Win32 handle-list attribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute). The console smoke captures process output and uses an isolated home containing spaces, Unicode and a trailing separator to exercise process lifetime and quoting on every runtime CI platform.

Security accounting uses the append-only `/v1.0/api/audit` route, the dashboard Security audit page, `arguebot audit list` and Ctrl+J in the TUI. Use `session.issue`, `session.revoke` or `http.authorization` as exact operation filters. Tenant administrators see their tenant; system administrators can inspect anonymous events too. Disabling request capture does not disable these security records. Database access can alter records outside the application's immutability boundary. Comprehensive credential/RBAC events and tamper-evident external retention remain open delivery work.

History compatibility uses attachment protocol 2, independent of database schema 10 and the unchanged product version. Stop protocol-1 services with a matching client or authenticated shutdown request before replacing binaries; protocol-2 local clients refuse an incompatible listener. History previews default to 4096 Unicode code points per message, configurable through HistoryPreviewCodePoints with range 128 through 16384 after restart. Full-message reads and exports retain complete stored content.

Event retention is enabled by default and does not delete transcript messages or audit records. Settings exposes EventRetentionEnabled, EventRetentionDays (1..3650), EventMaxPerConversation (1..1000000), EventMaintenanceSeconds (5..3600), EventRetentionBatchSize (1..1000) and EventReplayPageSize (1..1000). Save and restart to apply. Disabling maintenance leaves existing events intact. Pruning preserves the latest sequence anchor even when it is older than the age limit. Replay-gap and pruned-event counters use bounded provider labels. Maintenance scans eligible conversation metadata in bounded batches and prunes each conversation in its own writer transaction.

Manage tenant identities through Users, Ctrl+U or console `users` commands. Password exchange and service-wide rate limiting are described in [identity and sessions](IDENTITY.md). Hosted deployment remains gated.
