# REST API

The replacement service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in `IMPROVEMENTS.md` and `docs/DEVELOPMENT.md`.

Use `127.0.0.1` for local connections. The service is one open workspace: no accounts, roles, permissions, pairing, login, sessions or security audit API. Host/origin checks and service ownership remain in place.

## GET /health

Check readiness without authentication. Open workspace endpoint.

Success: **200**, response: `HealthResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| challenge | query | false | Optional fresh random 32-byte hex nonce. The listener returns homeProof bound to this challenge, actual port and private local-home key. This is local process attestation, not user authentication. |

## POST /v1.0/api/conversations

Create a persistent conversation without starting any model. Open workspace endpoint.

Success: **201**, response: `Conversation`. Request: `ConversationRequest`.

Representative request:

```json
{
  "title": "Example architecture decision"
}
```

## GET /v1.0/api/conversations

Search owned titles and message content with exact totals. Open workspace endpoint.

Success: **200**, response: `Page`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Backend filter; date/time values use UTC ISO 8601. |
| archived | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/conversations/{conversationId}

Read an owned conversation. Open workspace endpoint.

Success: **200**, response: `Conversation`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

## PUT /v1.0/api/conversations/{conversationId}

Change title, pin or archive using an expected revision. Open workspace endpoint.

Success: **200**, response: `Conversation`. Request: `ConversationRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

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

Read bounded owned message previews with stable membership and exact totals. Open workspace endpoint.

Success: **200**, response: `MessagePage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |
| throughSequence | query | false | Optional captured membership boundary. Omission captures the latest sequence. Streaming content itself is not frozen. Future boundaries are rejected. |
| tail | query | false | Select the last page of the captured membership, overriding pageNumber. Preview text is bounded; use individual message reads for full content. |

## POST /v1.0/api/conversations/{conversationId}/messages

Append a public note with command deduplication. Open workspace endpoint.

Success: **201**, response: `Message`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

Representative request:

```json
{
  "action": "note",
  "text": "Consider reversibility and ownership.",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/conversations/{conversationId}/events

Read committed events after a cursor; Accept text/event-stream enables resumable SSE. Open workspace endpoint.

Success: **200**, response: `List<DurableEvent>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| after | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |
| Accept | header | false | Protocol control. |
| Last-Event-ID | header | false | SSE cursor formatted conversationId:sequence; foreign conversation cursors fail. |

## POST /v1.0/api/participants

Create a reusable direct CLI or PolyPrompt completion profile. Open workspace endpoint.

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

List current runner profiles; excludes historical snapshots. Open workspace endpoint.

Success: **200**, response: `List<RunnerProfile>`.

## PUT /v1.0/api/participants/{profileId}

Edit a reusable profile without changing historical run snapshots. Open workspace endpoint.

Success: **200**, response: `RunnerProfile`. Request: `RunnerProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Identifier in this workspace. |

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

Read execution history and immutable participants. Open workspace endpoint.

Success: **200**, response: `List<DebateRun>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

## POST /v1.0/api/conversations/{conversationId}/runs

Start a debate or single-participant follow-up with a durable receipt. Open workspace endpoint. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `DebateRun`. Request: `CreateRunRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

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

Read a durable checkpoint and current revision. Open workspace endpoint.

Success: **200**, response: `DebateRun`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| runId | path | true | Identifier in this workspace. |

## POST /v1.0/api/conversations/{conversationId}/runs/{runId}/actions

Apply pause, resume, interrupt, stop, continue, extend, note, steer, hold or release. Open workspace endpoint. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| runId | path | true | Identifier in this workspace. |

Representative request:

```json
{
  "action": "stop",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/request-history

Inspect scoped request metadata with backend filtering and paging. Open workspace endpoint.

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

Return zero-filled UTC activity buckets for a bounded range. Open workspace endpoint.

Success: **200**, response: `List<ActivityBucket>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| fromUtc | query | true | Backend filter; date/time values use UTC ISO 8601. |
| toUtc | query | true | Backend filter; date/time values use UTC ISO 8601. |
| bucketMinutes | query | false | Fixed UTC interval in minutes. Dashboard presets: last hour 1 (60 buckets), last day 15 (96), last week 120 (84), rolling 30 days 480 (90). The range is [fromUtc, toUtc), limited to 31 days and 2000 buckets, including zero-count intervals. HTTP statuses below 400 count as success; 400 and above count as errors. Disabled request history returns an empty array. |

## GET /v1.0/api/request-history/{requestId}

Read redacted request and response headers and body prefixes. Open workspace endpoint.

Success: **200**, response: `RequestHistoryEntry`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Identifier in this workspace. |

## DELETE /v1.0/api/request-history/{requestId}

Delete one authorized retained request capture. Open workspace endpoint.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Identifier in this workspace. |

## GET /v1.0/api/settings

Read redacted file settings, revision and restart metadata. Open workspace endpoint.

Success: **200**, response: `SettingsView`.

## PUT /v1.0/api/settings

Atomically update allowlisted nonsecret settings using its revision. Open workspace endpoint. May execute work or change service state; do not blindly replay requests.

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

Drain workers and reload settings; native hosts restart in-process, containers exit for their supervisor. Open workspace endpoint. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `RestartResponse`. Request: `object`.

Representative request:

```json
{}
```

## DELETE /v1.0/api/participants/{profileId}

Delete a reusable participant, preserving historical snapshots. Open workspace endpoint.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Identifier in this workspace. |

## DELETE /v1.0/api/conversations/{conversationId}

Permanently delete a stopped conversation and its retained messages/events. Open workspace endpoint.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |

## POST /v1.0/api/settings/shutdown

Drain active work and stop the service; a supervisor may restart it. Open workspace endpoint.

Success: **202**, response: `Acknowledgement`.

## GET /v1.0/api/conversations/{conversationId}/runs/{runId}/attempts

Page durable generation attempts with frozen participant, prompt and context provenance. Open workspace endpoint.

Success: **200**, response: `AttemptPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| runId | path | true | Identifier in this workspace. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/conversations/{conversationId}/messages/{messageId}

Read one complete owned message without loading other history. Open workspace endpoint.

Success: **200**, response: `Message`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Identifier in this workspace. |
| messageId | path | true | Identifier in this workspace. |

## DTO schemas

Exact field types, defaults and validation bounds are in the checked-in OpenAPI schemas. The tables below describe known contracts; server state and validation remain authoritative. Diagnostic request/response bodies are explicitly unmanaged retained text, always subject to redaction/truncation and retention.

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

### AttemptPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching durable attempts. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |

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
| lastEventSequence | integer | Latest committed event sequence observed before this conversation read. Capture it before loading snapshots, then replay after it to cover concurrent changes. |
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
| question | string | Original conversation title frozen when this run was created. Renaming the conversation never changes historical prompts. Pre-schema-9 runs use the title available at migration. |
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
| textOffset | integer | UTF-16 code-unit offset for this committed text fragment within its message. Legacy events before schema 6 use zero and require snapshot reconciliation. |
| createdUtc | string | Creation time in UTC. |

### GenerationAttempt

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Attempt identifier. |
| conversationId | string | Containing conversation. |
| runId | string | Containing run. |
| messageId | string | Answer message, preserved even if generation is interrupted. |
| participantId | string | Immutable participant snapshot identifier. |
| number | integer | Monotonic attempt number within this run. |
| round | integer | Debate round at attempt acceptance. |
| speakerIndex | integer | Ordered participant position at acceptance. |
| state | string | Pending, streaming, completed, interrupted or failed. Acceptance precedes external invocation; interruption never proves the external operation did not run. |
| errorCode | string | Safe failure code without upstream response content. |
| promptVersion | string | Version of the deterministic system prompt template. |
| promptDigest | string | SHA-256 of the exact UTF-8 system prompt. |
| contextCharacters | integer | Exact UTF-16 character count of system prompt plus selected message content; this is not a token estimate. |
| durationMilliseconds | integer | Monotonic elapsed generation and durable stream flush time, zero if unavailable after a crash. |
| createdUtc | string | Acceptance time in UTC. |
| updatedUtc | string | Last lifecycle change in UTC. |
| contextMessageIds | array | Exact ordered source messages selected for this attempt, including any one-recipient directives. |

### HealthResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| homeId | string | Opaque path-bound application-home routing identifier. Not an authentication credential. |
| homeProof | string | HMAC-SHA256 process proof bound to an optional random challenge, listener port and home. Empty when no challenge was requested. |
| authenticationRequired | boolean | Whether HTTP requests require an enrolled session. True by default for compatibility with older services that omit this field. |
| status | string | Readiness state. |
| schema | integer | Current database schema version, independent of product version. |
| protocol | integer | HTTP attachment protocol version, separate from schema and product versions. |

### HistoryPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching captures. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |

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
| contentBytes | integer | Full stored UTF-8 content size. Bounded pages can return a shorter content preview with ContentTruncated true. |
| contentTruncated | boolean | Whether this transport result is a bounded preview. Fetch the individual message before treating its text as complete or exporting it. |
| recipientId | string (nullable) | Private directive recipient, if present. |
| state | string | Generation lifecycle state. |
| createdUtc | string | Creation time in UTC. |

### MessagePage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Ordered bounded message previews. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |
| throughSequence | integer | Membership boundary captured for this page. Reuse it to traverse a stable set while new messages arrive; streaming content itself can still advance. |
| latestSequence | integer | Latest stored sequence at request start, including messages beyond a supplied membership boundary. |

### Page

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching conversations. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matches. |

### RequestHistoryEntry

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable capture identifier. |
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

### RunnerProfile

CLI protocols are plain, claude, codex, gemini and mux. Named CLI providers fill omitted name, executable and empty arguments on create/update from assets/harness-defaults.json. Claude Code includes --dangerously-skip-permissions; Codex includes --yolo. Generic CLI (plain) requires an executable. CLI uses ordered arguments plus stdin. Mux defaults to print --output-format text --no-stats --no-session-persistence; custom arguments replace defaults. REST requires endpoint and model and supports native local tools (read_file, write_file, list_directory, run_process) when allowCli is enabled. workingDirectory applies to both runner kinds; empty uses the service directory. Tool activity is persisted as kind=tool messages, separate from answer text. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.

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
| workingDirectory | string | Directory on the execution host for CLI harnesses and REST local tools; empty uses the service directory. |
| secretEnvironmentVariable | string | Environment reference to credential; never its value. |
| timeoutSeconds | integer | Maximum generation seconds. |
| maxOutputBytes | integer | Maximum captured output bytes. |
| enabled | boolean | Whether new work can use this profile. |

### ServiceSettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| requestHistoryEnabled | boolean | Capture HTTP request history by default. |
| requestHistoryBodyBytes | integer | Capture prefix limit in bytes, default 32768. |
| requestHistoryRetentionDays | integer | Request history retention in days, default thirty. |
| database | DatabaseSettings | Persistent database configuration. SQLite is the local default. |
| tlsEnabled | boolean | Enable HTTPS using keys/listener.pfx in the application home. |
| tlsPasswordEnvironmentVariable | string | Optional environment variable containing the listener PFX password. No literal password is persisted. |
| port | integer | Loopback service port, default 4317, allowed 1 through 65535. |
| streamBatchCharacters | integer | Maximum accumulated stream characters before a durable commit. Default 4096, allowed one through 65536. |
| streamCommitMilliseconds | integer | Maximum stream timer interval in milliseconds, even when the runner stops emitting. Default 250, allowed ten through 5000. |
| eventRetentionEnabled | boolean | Enable background pruning of replay-event prefixes. Transcript and audit history are retained independently. Default true. |
| eventRetentionDays | integer | Replay age target in days, default thirty, range one through 3650. The latest event is always retained to preserve monotonic sequencing. |
| eventMaxPerConversation | integer | Maximum retained replay events per conversation, default 10000, range one through 1000000. |
| eventMaintenanceSeconds | integer | Background retention interval in seconds, default sixty, range five through 3600. |
| eventRetentionBatchSize | integer | Conversation metadata batch bound for retention, default one hundred, range one through one thousand. |
| eventReplayPageSize | integer | Default events per replay read, default one thousand, range one through one thousand. |
| historyPreviewCodePoints | integer | Maximum Unicode code points in each message preview returned by paged history. Default 4096, range 128 through 16384. Full message reads remain explicit. |
| contextMaxCharacters | integer | Maximum exact UTF-16 characters in the system prompt plus selected message content. Default 262144, range 1024 through 16777216. Oversized context pauses without invoking a model or truncating history. |
| contextMaxMessages | integer | Maximum selected messages per generation attempt. Default 1000, range one through 10000. |
| maxConcurrentRuns | integer | Default maximum concurrent conversations. |
| allowCli | boolean | Whether CLI harnesses and REST local file/process tools may execute. Default true for local mode only. |

### DatabaseSettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| type | DatabaseTypeEnum | Default embedded provider. |
| connectionString | string | SQLite path or server connection string without committed secrets. |
| connectionEnvironmentVariable | string | Optional environment variable that overrides ConnectionString. |

### SettingsUpdate

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| revision | string | Expected settings file revision. |
| tlsEnabled | boolean (nullable) | Optional HTTPS toggle. Provision keys/listener.pfx before enabling; restart to apply. |
| tlsPasswordEnvironmentVariable | string (nullable) | Optional listener PFX password environment reference; omission preserves it. |
| port | integer | Configured port, from 1 to 65535. Explicit launch flags still win. |
| maxConcurrentRuns | integer | Concurrent conversations, 1 to 64. |
| streamBatchCharacters | integer (nullable) | Optional stream batch threshold, one through 65536 characters. Omission preserves the configured value. |
| streamCommitMilliseconds | integer (nullable) | Optional stream flush interval, ten through 5000 milliseconds. Omission preserves the configured value. |
| eventRetentionEnabled | boolean (nullable) | Optional background replay retention toggle. Omission preserves its configured value. |
| eventRetentionDays | integer (nullable) | Optional replay age target, one through 3650 days. |
| eventMaxPerConversation | integer (nullable) | Optional retained event bound per conversation, one through 1000000. |
| eventMaintenanceSeconds | integer (nullable) | Optional retention interval, five through 3600 seconds. |
| eventRetentionBatchSize | integer (nullable) | Optional maintenance metadata batch size, one through one thousand. |
| eventReplayPageSize | integer (nullable) | Optional default replay size, one through one thousand. |
| historyPreviewCodePoints | integer (nullable) | Optional Unicode code-point limit per message preview, 128 through 16384. Omission preserves its configured value. |
| contextMaxCharacters | integer (nullable) | Optional context character budget, 1024 through 16777216. Omission preserves its configured value. |
| contextMaxMessages | integer (nullable) | Optional selected context message bound, one through 10000. Omission preserves its configured value. |
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

Send `Accept: text/event-stream` to the event route. Each committed change has an ID `conversationId:sequence`. Save the last processed ID and pass it in `Last-Event-ID` on reconnect. Heartbeats carry no application changes. Connections recycle after thirty seconds; Clients may use paged JSON events and snapshots when streaming is unavailable. Changes are read from committed storage, so replay and live delivery share one order. Clients must deduplicate IDs and refresh entity snapshots rather than append a repeated full message. Replay retention prunes only contiguous prefixes, preserving the latest sequence anchor. A nonzero expired cursor returns event_history_gap (HTTP 409 before streaming, or an error frame after headers). Capture lastEventSequence from the conversation, reload snapshots, then reconnect after that head. Transcripts are retained independently. Frames are bounded by the generated transport metadata in assets/transport.json.

Closing a client does not stop accepted work. Service shutdown and crashes leave uncertain generations interrupted and their runs paused. Resume/retry is explicit. Historical participant snapshots survive profile edits/deletion. REST models can call service-owned local file and process tools when allowCli is enabled. Calls and results are persisted as tool messages and replay through normal history/events. Stopping cancels local processes. Tool execution has a 32-call/16-round turn limit, 30-second process limit and 64-KiB file/pipe limits. Interrupted operations may have side effects; inspect before explicitly retrying.
