# REST API

The replacement service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in `IMPROVEMENTS.md` and `docs/DEVELOPMENT.md`.

Use `127.0.0.1` for local connections. Exchange the owner-readable single-use pairing code for an opaque one-hour session, then send `Authorization: Bearer <token>`. Keys live outside the database. Tokens are revocable and never belong in URL parameters. `x-token` may carry the identical session; conflicting carriers and tenant hints are rejected. Password/credential exchange is not yet enabled by this local-only host.

List routes use server-side filters with `pageNumber`, `pageSize`, `totalCount` and ordered items where the response is a page. Conversation search uses literal case-insensitive substring matching across titles and message content; pagination never changes its scope. Conversation mutations use expected revisions. Note/run creation and durable moderator controls use idempotency keys, bounded to 200 characters. An exact committed retry succeeds before stale-revision/state checks; a reused key with different input or run returns 409. Run checkpoints, status events and moderator receipts commit atomically. Hold/release are transient per-client leases. Startup pauses uncertain work and completes a committed stop intent without automatically replaying generation.

Standard failures use `ApiError`: stable `code`, safe `message`, and `requestId`. Typical statuses are 400 validation, 401 missing/expired/revoked session, 403 insufficient permission or conflicting tenant hint, 404 unowned/missing resource, 409 state/revision/key conflict, 413 request body limit, 429 enrollment limit, and 500 internal failure. Request bodies are bounded to one MiB. Do not automatically retry an uncertain generation.

## GET /health

Check readiness without authentication. Public local endpoint. Read permission required.

Success: **200**, response: `HealthResponse`.

## POST /v1.0/api/auth/pair

Exchange the current single-use local pairing code. Public local endpoint. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `PairResponse`. Request: `PairRequest`.

Representative request:

```json
{
  "code": "{{pairingCode}}"
}
```

## GET /v1.0/api/auth/me

Read the active principal and tenant. Requires an active bearer session. Read permission required.

Success: **200**, response: `RequestContext`.

## POST /v1.0/api/conversations

Create a persistent conversation without starting any model. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **201**, response: `Conversation`. Request: `ConversationRequest`.

Representative request:

```json
{
  "title": "Example architecture decision"
}
```

## GET /v1.0/api/conversations

Search owned titles and message content with exact totals. Requires an active bearer session. Read permission required.

Success: **200**, response: `Page`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Backend filter; date/time values use UTC ISO 8601. |
| archived | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/conversations/{conversationId}

Read an owned conversation. Requires an active bearer session. Read permission required.

Success: **200**, response: `Conversation`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

## PUT /v1.0/api/conversations/{conversationId}

Change title, pin or archive using an expected revision. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `Conversation`. Request: `ConversationRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "title": "Updated example decision",
  "pinned": true,
  "archived": false,
  "revision": "{{conversationRevision}}"
}
```

## GET /v1.0/api/conversations/{conversationId}/messages

Read ordered owned history including owner-visible directives. Requires an active bearer session. Read permission required.

Success: **200**, response: `List<Message>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

## POST /v1.0/api/conversations/{conversationId}/messages

Append a public note with command deduplication. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **201**, response: `Message`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "action": "note",
  "text": "Consider reversibility and ownership.",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/conversations/{conversationId}/events

Read committed events after a cursor; Accept text/event-stream enables resumable SSE. Requires an active bearer session. Read permission required.

Success: **200**, response: `List<DurableEvent>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |
| after | query | false | Backend filter; date/time values use UTC ISO 8601. |
| Accept | header | false | Protocol control. |
| Last-Event-ID | header | false | SSE cursor formatted conversationId:sequence; foreign conversation cursors fail. |

## POST /v1.0/api/participants

Create a reusable direct CLI or PolyPrompt completion profile. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **201**, response: `RunnerProfile`. Request: `RunnerProfile`.

Representative request:

```json
{
  "name": "Example REST participant",
  "kind": "rest",
  "provider": "ollama",
  "endpoint": "{{simulationEndpoint}}",
  "model": "fixture",
  "timeoutSeconds": 5
}
```

## GET /v1.0/api/participants

List current runner profiles; excludes historical snapshots. Requires an active bearer session. Read permission required.

Success: **200**, response: `List<RunnerProfile>`.

## PUT /v1.0/api/participants/{profileId}

Edit a reusable profile without changing historical run snapshots. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `RunnerProfile`. Request: `RunnerProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "name": "Updated example participant",
  "kind": "rest",
  "provider": "ollama",
  "endpoint": "{{simulationEndpoint}}",
  "model": "fixture",
  "enabled": true
}
```

## GET /v1.0/api/conversations/{conversationId}/runs

Read execution history and immutable participants. Requires an active bearer session. Read permission required.

Success: **200**, response: `List<DebateRun>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

## POST /v1.0/api/conversations/{conversationId}/runs

Start a debate or single-participant follow-up with a durable receipt. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `DebateRun`. Request: `CreateRunRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "mode": "followup",
  "question": "Summarize the options.",
  "participantIds": [
    "{{profileId}}"
  ],
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/conversations/{conversationId}/runs/{runId}

Read a durable checkpoint and current revision. Requires an active bearer session. Read permission required.

Success: **200**, response: `DebateRun`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |
| runId | path | true | Identifier in the authenticated ownership scope. |

## POST /v1.0/api/conversations/{conversationId}/runs/{runId}/actions

Apply pause, resume, interrupt, stop, continue, extend, note, steer, hold or release. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `Acknowledgement`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |
| runId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "action": "stop",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/request-history

Inspect scoped request metadata with backend filtering and paging. Requires an active bearer session. Read permission required.

Success: **200**, response: `HistoryPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Backend filter; date/time values use UTC ISO 8601. |
| method | query | false | Backend filter; date/time values use UTC ISO 8601. |
| status | query | false | Backend filter; date/time values use UTC ISO 8601. |
| fromUtc | query | false | Backend filter; date/time values use UTC ISO 8601. |
| toUtc | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/request-history/summary

Return zero-filled UTC activity buckets for a bounded range. Requires an active bearer session. Read permission required.

Success: **200**, response: `List<ActivityBucket>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| fromUtc | query | true | Backend filter; date/time values use UTC ISO 8601. |
| toUtc | query | true | Backend filter; date/time values use UTC ISO 8601. |
| bucketMinutes | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/request-history/{requestId}

Read redacted request and response headers and body prefixes. Requires an active bearer session. Read permission required.

Success: **200**, response: `RequestHistoryEntry`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Identifier in the authenticated ownership scope. |

## DELETE /v1.0/api/request-history/{requestId}

Delete one authorized retained request capture. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Identifier in the authenticated ownership scope. |

## GET /v1.0/api/settings

Read redacted file settings, revision and restart metadata. Requires an active bearer session. Read permission required.

Success: **200**, response: `SettingsView`.

## PUT /v1.0/api/settings

Atomically update allowlisted nonsecret settings using its revision. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `SettingsView`. Request: `SettingsUpdate`.

Representative request:

```json
{
  "revision": "{{settingsRevision}}",
  "port": 4317,
  "maxConcurrentRuns": 4,
  "allowCli": true,
  "databaseType": "Sqlite",
  "connectionEnvironmentVariable": ""
}
```

## POST /v1.0/api/settings/restart

Drain workers and reload settings; native hosts restart in-process, containers exit for their supervisor. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `RestartResponse`. Request: `object`.

Representative request:

```json
{}
```

## DELETE /v1.0/api/participants/{profileId}

Delete a reusable participant, preserving historical snapshots. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Identifier in the authenticated ownership scope. |

## DELETE /v1.0/api/conversations/{conversationId}

Permanently delete a stopped conversation and its retained messages/events. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in the authenticated ownership scope. |

## POST /v1.0/api/settings/shutdown

Drain active work and stop the service; a supervisor may restart it. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `Acknowledgement`.

## POST /v1.0/api/auth/logout

Revoke the current session immediately. Requires an active bearer session. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `RevocationResponse`. Request: `object`.

Representative request:

```json
{}
```

## DTO schemas

Exact field types, defaults and validation bounds are in the checked-in OpenAPI schemas. The tables below describe known contracts; server state and authorization remain authoritative. Diagnostic request/response bodies are explicitly unmanaged retained text, always subject to redaction/truncation and retention.

### Acknowledgement

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| accepted | boolean | The command was accepted. |

### ActivityBucket

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| startUtc | string | UTC bucket boundary. |
| success | integer | Nonerror requests in bucket. |
| failure | integer | Failed requests in bucket. |

### ApiError

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| code | string | Stable error code. |
| message | string | Safe explanation. |
| requestId | string | Correlation identifier. |

### CapturedHeader

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Header name. |
| value | string | Redacted header value. |
| response | boolean | Whether this is a response header. |

### CommandRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| action | string | Requested operation. |
| text | string | User text. |
| recipientId | string (nullable) | Target profile. |
| idempotencyKey | string | Unique command key, 1 to 200 characters for durable controls and messages; hold/release are transient. |
| revision | integer (nullable) | Expected resource revision. |
| rounds | integer | Extension rounds. |

### Conversation

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable conversation identifier. |
| tenantId | string | Owning tenant. |
| userId | string | Owning user. |
| title | string | Display title. |
| pinned | boolean | Whether pinned. |
| archived | boolean | Whether archived. |
| revision | integer | Optimistic revision. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### ConversationRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| title | string | Conversation title/topic. |
| pinned | boolean | Pinned state. |
| archived | boolean | Archived state. |
| revision | integer (nullable) | Expected revision. |

### CreateRunRequest

Debate requires two or three distinct participant IDs. Followup requires one participant and a nonempty question, and forces one round with no countdown.

| Field | Type | Meaning |
| --- | --- | --- |
| mode | string | Run purpose: debate or followup. Follow-up uses exactly one participant. |
| question | string | Follow-up question, persisted as a message with this run. |
| participantIds | array | Ordered profiles. |
| maxRounds | integer | Round limit. |
| interventionSeconds | integer | Intervention countdown. |
| idempotencyKey | string | Unique command key. |

### DebateRun

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| mode | string | Run purpose derived from its immutable participant count. |
| id | string | Stable execution identifier. |
| conversationId | string | Containing conversation. |
| state | string | Execution state. |
| outcome | string | Completion reason. |
| round | integer | Current round. |
| speakerIndex | integer | Next participant index. |
| maxRounds | integer | Round limit. |
| interventionSeconds | integer | Pause between turns. |
| revision | integer | Optimistic run revision. |
| participants | array | Immutable runner snapshots. |
| createdUtc | string | Creation time in UTC. |

### DeletionResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| deleted | boolean | The owned record was deleted. |

### DurableEvent

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| sequence | integer | Durable conversation event order. |
| conversationId | string | Containing conversation. |
| kind | string | Semantic event type. |
| entityId | string | Changed entity. |
| text | string | Optional text delta or safe detail. |
| createdUtc | string | Creation time in UTC. |

### HealthResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| status | string | Readiness state. |
| schema | integer | Current database schema version, independent of product version. |
| protocol | integer | HTTP attachment protocol version, separate from schema and product versions. |

### HistoryFilter

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| search | string | Path search. |
| method | string | Optional HTTP method. |
| status | integer (nullable) | Optional exact response status. |
| fromUtc | string (nullable) | Optional start UTC. |
| toUtc | string (nullable) | Optional end UTC. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size, 1 to 100. |

### HistoryPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching captures. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |

### LocalServiceDescriptor

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| endpoint | string | Local listener address. |
| protocol | integer | Attachment protocol, independent of product and schema versions. |
| instanceId | string | Process instance identifier used when removing stale metadata. |
| processId | integer | Operating-system process ID for diagnostics, never a substitute for the ownership lock. |
| startedUtc | string | Service startup time. |

### Message

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable message identifier. |
| conversationId | string | Containing conversation. |
| runId | string (nullable) | Optional run. |
| sequence | integer | Conversation message order. |
| speaker | string | Speaker snapshot. |
| kind | string | Semantic message kind. |
| content | string | Original message text. |
| recipientId | string (nullable) | Private directive recipient, if present. |
| state | string | Generation lifecycle state. |
| createdUtc | string | Creation time in UTC. |

### Page

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching conversations. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matches. |

### RequestContext

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| receivedUtc | string | UTC request receipt time. |
| startedTimestamp | integer | Monotonic timestamp captured before route execution. |
| tenantId | string | Resolved tenant. |
| userId | string | Authenticated user. |
| isAdmin | boolean | System administrator flag. |
| isTenantAdmin | boolean | Tenant administrator flag. |
| isAuthenticated | boolean | Whether credentials are valid. |
| role | string | Effective local role. |
| correlationId | string | Request correlation identifier. |

### RequestHistoryEntry

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable capture identifier. |
| tenantId | string (nullable) | Resolved tenant, or null for anonymous system traffic. |
| userId | string (nullable) | Resolved user if authenticated. |
| principalName | string | Safe principal display name. |
| method | string | HTTP method. |
| path | string | Route path without query credentials. |
| status | integer | HTTP response status. |
| durationMs | integer | Elapsed milliseconds. |
| requestBytes | integer | Original request size. |
| responseBytes | integer | Original response size where known; minus one means streamed or unknown. |
| rawRequestBody | string | Redacted diagnostic request prefix; explicitly unmanaged content. |
| rawResponseBody | string | Redacted diagnostic response prefix; explicitly unmanaged content. |
| requestTruncated | boolean | Whether request capture is a prefix. |
| responseTruncated | boolean | Whether response capture is a prefix. |
| correlationId | string | Request correlation ID. |
| traceId | string | Trace correlation ID. |
| createdUtc | string | Request receipt timestamp. |
| headers | array | Redacted structured headers. |

### RestartResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| restarting | boolean | The host is draining and reloading configuration. |

### RevocationResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| revoked | boolean | The session was revoked. |

### RunnerProfile

CLI requires executable and uses ordered arguments plus stdin. REST requires endpoint and model. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable profile identifier. |
| name | string | Participant display name. |
| kind | string | Runner kind: cli or rest. |
| provider | string | Protocol adapter. |
| endpoint | string | REST base URI. |
| model | string | REST model identifier. |
| executable | string | Direct executable path or name. |
| arguments | array | Ordered arguments; prompts use standard input. |
| workingDirectory | string | Directory on execution host. |
| secretEnvironmentVariable | string | Environment reference to credential; never its value. |
| timeoutSeconds | integer | Maximum generation seconds. |
| maxOutputBytes | integer | Maximum captured output bytes. |
| enabled | boolean | Whether new work can use this profile. |

### ArgueBotPaths

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| home | string | Absolute application home. |
| databaseFile | string | Default SQLite file. |
| settingsFile | string | Service settings file. |

### ServiceSettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| telemetry | TelemetrySettings | Process telemetry collection and export settings. |
| requestHistoryEnabled | boolean | Capture HTTP request history by default. |
| requestHistoryBodyBytes | integer | Capture prefix limit in bytes, default 32768. |
| requestHistoryRetentionDays | integer | Request history retention in days, default thirty. |
| database | DatabaseSettings | Persistent database configuration. SQLite is the local default. |
| port | integer | Loopback service port, default 4317, allowed 1 through 65535. |
| maxConcurrentRuns | integer | Default maximum concurrent conversations. |
| allowCli | boolean | Whether shell-out runners may execute. Default true for local mode only. |

### TelemetrySettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| enabled | boolean | Enable the collector subscription, default true. |
| exportEnabled | boolean | Enable outbound OTLP to the local observability collector, default false outside Compose. |
| endpoint | string | OTLP gRPC endpoint, default 127.0.0.1:4327 to avoid the application port. |

### DatabaseSettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| type | DatabaseTypeEnum | Default embedded provider. |
| connectionString | string | SQLite path or server connection string without committed secrets. |
| connectionEnvironmentVariable | string | Optional environment variable that overrides ConnectionString. |

### SchemaMigration

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| version | integer | Monotonic database schema version, independent of product version. |
| description | string | Human-readable upgrade purpose. |
| statements | array | Ordered parameterized DDL statements. |

### PairRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| code | string | Current single-use code read locally by the owner. |

### PairResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| token | string | Opaque one-hour bearer session token. |

### SettingsUpdate

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| revision | string | Expected settings file revision. |
| port | integer | Configured port, from 1 to 65535. Explicit launch flags still win. |
| maxConcurrentRuns | integer | Concurrent conversations, 1 to 64. |
| allowCli | boolean | Allow local shell-out execution after restart. |
| databaseType | DatabaseTypeEnum | Selected persistence provider. |
| connectionEnvironmentVariable | string | Server environment reference for the connection string. |

### SettingsView

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| settings | ServiceSettings | Editable nonsecret settings. Connection strings are never returned. |
| revision | string | Digest of the current settings file for optimistic writes. |
| restartRequired | boolean | Whether this file differs from the running configuration. |
| portOverridden | boolean | Whether the running port is overridden by an explicit command-line flag. |
| effectivePort | integer | Effective port of this process. |
| connectionConfigured | boolean | Whether a private database connection string is configured. |

## Streaming and recovery

Send `Accept: text/event-stream` and the same bearer header to the event route. Each committed change has an ID `conversationId:sequence`. Save the last processed ID and pass it in `Last-Event-ID` on reconnect. Heartbeats carry no application changes. Connections recycle after thirty seconds; `session_revoked` terminates access. Clients may use paged JSON events and snapshots when streaming is unavailable. Changes are read from committed storage, so replay and live delivery share one order. Clients must deduplicate IDs and refresh entity snapshots rather than append a repeated full message. Event pruning and explicit retention-gap snapshots remain an open implementation gate.

Closing a client does not stop accepted work. Service shutdown and crashes leave uncertain generations interrupted and their runs paused. Resume/retry is explicit. Historical participant snapshots survive profile edits/deletion. CLI and REST runs have separate capabilities; REST tool execution is disabled.
