# Local service operations

The replacement service binds to 127.0.0.1 and owns its application home exclusively. Start it with `dotnet run --project src/ArgueBot.Server -c Release -- --home .run/home`. Production hosted access, complete identity administration, distribution packages, and release acceptance remain unfinished; see IMPROVEMENTS.md.

Settings live in `arguebot.json` beneath the selected home. Configuration precedence is `--home`, `ARGUEBOT_HOME`, then the operating system profile's `.arguebot` directory. `--port` overrides the configured listener port for the process. The authenticated Settings page displays the effective port and whether the saved configuration requires restart. Save uses a revision precondition and atomic file replacement. Restart detaches HTTP clients, drains workers, releases ownership, and reloads configuration. In containers the process exits for a supervisor instead of launching a second copy. Keep an explicit port override in mind when testing a saved listener change.

SQLite is the default. Startup refuses a future schema instead of downgrading it, and a supported historical SQLite upgrade creates a backup first. Existing server-provider schemas currently require an operator-managed verified backup before upgrade. Native server-provider backup/restore and portable encrypted bundle workflows are still being implemented. Do not point development fixtures at an existing installation.

The local pairing code is owner-readable and rotates after use. Sessions expire after one hour and can be revoked immediately. AES-GCM session encryption keys live in `keys`; the database stores session digests. A filesystem backup of only the database omits settings and keys. Do not delete retained keys while sessions issued under them remain valid. Full recovery bundles and key retention tooling are pending.

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

Local JSONL operational logs live in `logs/operations.jsonl`, with four rotated files and a 5 MiB rotation threshold. File logs are written at information level and above, with owner-only mode on Unix. Capture failures use a separate best-effort type-only log. Loki retains seven days; Tempo retains one day. Compose metric storage uses Prometheus defaults. Disk quota policies, configurable file-log retention, complete administrative log browsing and protected audit coverage remain open work.

`docker compose down` stops this stack and preserves its named volumes. Do not add `-v` unless intentionally discarding its historical telemetry. Configuration files are mounted read-only; restart the affected component after editing them. The image wrappers include cURL so healthchecks use the required five-second interval, timeout no greater than five seconds, and two consecutive failures. Docker does not implement a two-success recovery hysteresis.
