# REST API

The replacement service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in `IMPROVEMENTS.md` and `docs/DEVELOPMENT.md`.

Use `127.0.0.1` for local connections. Exchange the owner-readable single-use pairing code for an opaque one-hour session, then send `Authorization: Bearer <token>`. Keys live outside the database. Tokens are revocable and never belong in URL parameters. `x-token` may carry the identical session; conflicting carriers, repeated/empty authentication headers and tenant hints are rejected. `x-tenant-guid` and `x-tenant-id` must agree with the session. Unrelated password, access-key and signature material is rejected, including alongside a valid session. Password/credential exchange is not yet enabled by this local-only host.

List routes use server-side filters with `pageNumber`, `pageSize`, `totalCount` and ordered items where the response is a page. Conversation search uses literal case-insensitive substring matching across titles and message content; pagination never changes its scope. Conversation mutations use expected revisions. Note/run creation and durable moderator controls use idempotency keys, bounded to 200 characters. An exact committed retry succeeds before stale-revision/state checks; a reused key with different input or run returns 409. Run checkpoints, status events and moderator receipts commit atomically. Hold/release are transient per-client leases. Startup pauses uncertain work and completes a committed stop intent without automatically replaying generation.

Recovery archives and operations are protected global resources requiring an authenticated system administrator. Jobs return 202 before completion; poll `/v1.0/api/backup-operations/{operationId}`. Passphrases are not persisted in operation metadata. Controlled archive restore creates a new home beneath the service home and leaves the recovered service stopped. SQLite creates a new file; PostgreSQL requires an explicitly provisioned empty database through restoreConnectionEnvironmentVariable. Existing homes and nonempty destination databases are refused. If an external restore commits but reconciliation or publication fails, retainedStagingHome identifies protected operator recovery state. Binary download uses the existing bearer header. See `docs/RECOVERY.md`.

Streaming commits are batched by a configured size threshold or timer. Schema-6 events carry `textOffset` in UTF-16 code units; reconcile older events from a message snapshot. Finalization flushes pending fragments before its terminal event. Standard failures use `ApiError`: stable `code`, safe `message`, and `requestId`. Typical statuses are 400 validation, 401 missing/expired/revoked session, 403 insufficient permission or conflicting tenant hint, 404 unowned/missing resource, 409 state/revision/key conflict, 413 request body limit, 429 enrollment limit, and 500 internal failure. Request bodies are bounded to one MiB. Do not automatically retry an uncertain generation.

## GET /health

Check readiness without authentication. Public local endpoint. Read permission required.

Success: **200**, response: `HealthResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| challenge | query | false | Optional fresh random 32-byte hex nonce. The listener returns homeProof bound to this challenge, actual port and private local-home key. This is local process attestation, not user authentication. |

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

## GET /v1.0/api/backups

List global recovery artifacts. Requires an active bearer session. Read permission required.

Success: **200**, response: `BackupPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## POST /v1.0/api/backups

Create complete recovery archive. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `BackupOperation`. Request: `BackupRequest`.

Representative request:

```json
{
  "name": "Example recovery",
  "includeSecrets": false
}
```

## GET /v1.0/api/backups/{backupId}

Read recovery artifact. Requires an active bearer session. Read permission required.

Success: **200**, response: `BackupEntry`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | path | true | Identifier in the authenticated ownership scope. |

## POST /v1.0/api/backups/{backupId}/verify

Verify a recovery archive. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `BackupOperation`. Request: `BackupRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{}
```

## POST /v1.0/api/backups/{backupId}/restore

Restore into a new isolated home. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **202**, response: `BackupOperation`. Request: `BackupRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | path | true | Identifier in the authenticated ownership scope. |

Representative request:

```json
{
  "destinationName": "recovered-example",
  "port": 4537
}
```

## GET /v1.0/api/backups/{backupId}/download

Download a recovery archive. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Read permission required.

Success: **200**, response: `BinaryArchive`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | path | true | Identifier in the authenticated ownership scope. |

## DELETE /v1.0/api/backups/{backupId}

Delete an idle recovery archive. Requires an active bearer session. May execute work or change service state; do not blindly replay requests. Mutation permission required; service settings/restart require administrator.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | path | true | Identifier in the authenticated ownership scope. |

## GET /v1.0/api/backup-operations/{operationId}

Read recovery operation status. Requires an active bearer session. Read permission required.

Success: **200**, response: `BackupOperation`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| operationId | path | true | Identifier in the authenticated ownership scope. |

## GET /v1.0/api/backup-operations

Page durable recovery jobs. Requires an active bearer session. Read permission required.

Success: **200**, response: `BackupOperationPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| backupId | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

## GET /v1.0/api/audit

Inspect append-only tenant security events; system administrators can inspect all tenants. Requires an active bearer session. Read permission required.

Success: **200**, response: `AuditPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| operation | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageNumber | query | false | Backend filter; date/time values use UTC ISO 8601. |
| pageSize | query | false | Backend filter; date/time values use UTC ISO 8601. |

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

### AuditEntry

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Immutable event identifier. |
| tenantId | string (nullable) | Resolved tenant, or null for unauthenticated/system events. |
| userId | string (nullable) | Validated actor, never an untrusted request hint. |
| operation | string | Bounded semantic security operation. |
| outcome | string | Semantic result without credentials or request content. |
| sessionId | string | Session identifier where known; never the token. |
| authScheme | string | Validated authentication carrier where known. |
| correlationId | string | Request correlation identifier where available. |
| resourceType | string | Semantic resource type. |
| resourceId | string | Target identifier where known. |
| errorCode | string | Safe failure code, empty on success. |
| createdUtc | string | Event timestamp in UTC. |

### AuditPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching immutable audit events. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |

### BackupEntry

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Opaque artifact identifier. |
| name | string | Operator's display name. |
| bytes | integer | Published archive size. |
| encrypted | boolean | Whether the archive is passphrase encrypted. |
| includesSecrets | boolean | Whether explicitly requested key material is included. |
| schemaVersion | integer | Captured schema version. |
| provider | string | Source storage provider. |
| digest | string | Published archive SHA-256. |
| status | string | Creating, ready, failed or unavailable. |
| createdUtc | string | UTC creation time. |

### BackupOperation

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Opaque operation identifier. |
| backupId | string | Associated artifact. |
| kind | string | Create, verify or restore. |
| state | string | Queued, running, completed or failed. |
| errorCode | string | Safe failure code, without exception details or credentials. |
| targetHome | string | Explicit new-home restore destination, otherwise empty. |
| targetProvider | string | Restore destination provider, otherwise empty. |
| targetConnectionEnvironmentVariable | string | Nonsecret PostgreSQL destination environment reference, otherwise empty. |
| retainedStagingHome | string | Protected staging home retained after an externally committed database could not be reconciled or published. Requires explicit operator recovery. |
| port | integer | Requested listener port, or the effective port after successful restoration. Zero initially preserves the archive setting. |
| missingReferences | array | Credential names missing during restore. |
| createdUtc | string | UTC acceptance time. |
| updatedUtc | string | UTC last status change. |

### BackupOperationPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching items in stable newest-first order. |
| totalCount | integer | Matching total independent of page size. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Items per page. |

### BackupPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching items in stable newest-first order. |
| totalCount | integer | Matching total independent of page size. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Items per page. |

### BackupRequest

Create accepts name/includeSecrets/password. Verify accepts password. Restore accepts password/destinationName/port/restoreDatabaseType/restoreConnectionEnvironmentVariable. Secret-inclusive creation requires encryption. Port zero retains the archived listener value; otherwise use 1024 through 65535. SQLite creates a new database file; PostgreSQL requires an empty pre-provisioned destination configured through a service-machine environment reference.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Display name for a newly created artifact, up to 200 characters. |
| includeSecrets | boolean | Include local encryption keys and literal database configuration. Requires a passphrase. |
| password | string | Optional encryption passphrase. A nonempty value requires at least twelve characters and at most 1024 UTF-8 bytes; never persisted in job metadata. |
| destinationName | string | Required for restore. New child directory beneath the service home restores directory. |
| restoreDatabaseType | DatabaseTypeEnum | Destination provider for a new recovered home. SQLite is the default; PostgreSQL requires an empty pre-provisioned database. |
| restoreConnectionEnvironmentVariable | string | Environment reference on the service machine containing the PostgreSQL destination connection string. Never send its secret value. |
| port | integer | Listener port for a new recovered home. Zero preserves the archived setting. |

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
| textOffset | integer | UTF-16 code-unit offset for this committed text fragment within its message. Legacy events before schema 6 use zero and require snapshot reconciliation. |
| createdUtc | string | Creation time in UTC. |

### HealthResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| homeId | string | Opaque path-bound application-home routing identifier. Not an authentication credential. |
| homeProof | string | HMAC-SHA256 process proof bound to an optional random challenge, listener port and home. Empty when no challenge was requested. |
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
| sessionId | string | Resolved revocable session identifier; empty for non-session internal calls. |
| authScheme | string | Validated transport carrier: BearerToken or XToken for enrolled HTTP sessions. |
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
| streamBatchCharacters | integer | Maximum accumulated stream characters before a durable commit. Default 4096, allowed one through 65536. |
| streamCommitMilliseconds | integer | Maximum stream timer interval in milliseconds, even when the runner stops emitting. Default 250, allowed ten through 5000. |
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
| streamBatchCharacters | integer (nullable) | Optional stream batch threshold, one through 65536 characters. Omission preserves the configured value. |
| streamCommitMilliseconds | integer (nullable) | Optional stream flush interval, ten through 5000 milliseconds. Omission preserves the configured value. |
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

## Security accounting

The append-only audit route permits tenant administrators to inspect their own tenant and system administrators to inspect all events, including anonymous failures. Session issuance/revocation and HTTP 401/403 denials are recorded independently of optional request capture. Audit inspection supports an exact operation filter and bounded paging. No HTTP edit/delete route exists. Direct privileged database access is outside application-level immutability; comprehensive credential/RBAC/administrative accounting remains under implementation.

## Streaming and recovery

Send `Accept: text/event-stream` and the same bearer header to the event route. Each committed change has an ID `conversationId:sequence`. Save the last processed ID and pass it in `Last-Event-ID` on reconnect. Heartbeats carry no application changes. Connections recycle after thirty seconds; `session_revoked` terminates access. Clients may use paged JSON events and snapshots when streaming is unavailable. Changes are read from committed storage, so replay and live delivery share one order. Clients must deduplicate IDs and refresh entity snapshots rather than append a repeated full message. Event pruning and explicit retention-gap snapshots remain an open implementation gate.

Closing a client does not stop accepted work. Service shutdown and crashes leave uncertain generations interrupted and their runs paused. Resume/retry is explicit. Historical participant snapshots survive profile edits/deletion. CLI and REST runs have separate capabilities; REST tool execution is disabled.
