# REST API

The ArgueBot service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations remain in [ROADMAP.md](ROADMAP.md) and [development documentation](docs/DEVELOPMENT.md).

## Authentication

Every route except `/health`, `GET /v2.0/api/login-info`, `POST /v2.0/api/token`, the OpenAPI document and the dashboard files needs a bearer token. Send it as `Authorization: Bearer <token>` or as `x-token: <token>`; if you send both, they must be the same token.

- **Session token**: `POST /v2.0/api/token` with `{ "email", "password", "tenantId" }`, or with the `x-email`, `x-password` and optional `x-tenant-id` headers. The response holds an opaque `abs_...` token that lasts 60 minutes. `POST /v2.0/api/token/refresh` replaces it with a new token and a fresh lifetime; the previous token stops working. `DELETE /v2.0/api/token` signs out.
- **Credential token**: a long-lived `abt_...` token created under `/v2.0/api/me/credentials`. It is shown once. Disable or delete the credential to revoke it.
- **Default administrator**: `admin@arguebot` with password `password` and bearer token `arguebotadmin`. The sign-in page always shows these. Change the password and create your own credentials, then disable nothing you still need: the default user, tenant and token are protected and cannot be deleted.

If the email and password match accounts in several tenants, sign-in returns 409 `tenant_required` with the tenants to choose from; send `tenantId` and sign in again. `x-tenant-id` on other routes must match the token's tenant (403 `tenant_mismatch`), except that a system administrator may use it on user, session and audit routes to act in another tenant.

Failures: 401 `authentication_required`, `invalid_token`, `session_expired`, `session_revoked`, `credential_inactive`, `credential_expired`, `user_inactive`, `tenant_inactive`, `invalid_credentials`, `account_inactive`, `conflicting_credentials`, `unsupported_scheme` or `password_not_accepted`; 403 `forbidden` or `tenant_mismatch` with an `AuthorizationError` body; 429 `rate_limited` with `Retry-After` after 10 failed sign-ins from one address or for one email in 5 minutes. Every failure and every administrative change is recorded in the audit log (`GET /v2.0/api/audit`).

Access classes: **Member** routes are open to any signed-in user of an active tenant, and data is shared within the tenant. **TenantAdmin** routes need a tenant administrator. **SystemAdmin** routes need a system administrator. Each route below states its class.

Host names: `127.0.0.1` and `localhost` are always accepted. Add other names, such as a reverse proxy name, to `allowedHosts` in the service settings or to `ARGUEBOT_ALLOWED_HOSTS`. Use TLS, directly or through a TLS-terminating proxy, whenever the service is reachable from another machine.

## GET /health

Check readiness without authentication. Access: public; no bearer token.

Success: **200**, response: `HealthResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| challenge | query | false | Optional fresh random 32-byte hex nonce. The listener returns homeProof bound to this challenge, actual port and private local-home key. This is local process attestation, not user authentication. |

## POST /v1.0/api/conversations

Create a persistent conversation without starting any model. Access: any signed-in user of an active tenant.

Success: **201**, response: `Conversation`. Request: `ConversationRequest`.

Representative request:

```json
{
  "title": "Billing isolation",
  "openingQuestion": "Should billing move to its own service this quarter?"
}
```

## GET /v1.0/api/conversations

Search owned titles and message content with exact totals. Access: any signed-in user of an active tenant.

Success: **200**, response: `Page`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Case-insensitive text to find in paths or titles. |
| archived | query | false | true lists archived conversations instead of active ones. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v1.0/api/conversations/{conversationId}

Read an owned conversation. Access: any signed-in user of an active tenant.

Success: **200**, response: `Conversation`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## PUT /v1.0/api/conversations/{conversationId}

Change title, pin or archive using an expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `Conversation`. Request: `ConversationRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

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

Read bounded owned message previews with stable membership and exact totals. Access: any signed-in user of an active tenant.

Success: **200**, response: `MessagePage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |
| throughSequence | query | false | Optional captured membership boundary. Omission captures the latest sequence. Streaming content itself is not frozen. Future boundaries are rejected. |
| tail | query | false | Select the last page of the captured membership, overriding pageNumber. Preview text is bounded; use individual message reads for full content. |

## POST /v1.0/api/conversations/{conversationId}/messages

Append a public note with command deduplication. Access: any signed-in user of an active tenant.

Success: **201**, response: `Message`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "action": "note",
  "text": "Consider reversibility and ownership.",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/conversations/{conversationId}/events

Read committed events after a cursor; Accept text/event-stream enables resumable SSE. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DurableEvent>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| after | query | false | Exclusive sequence cursor: return items after this sequence number. Use the last sequence you received. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |
| Accept | header | false | Protocol control. |
| Last-Event-ID | header | false | SSE cursor formatted conversationId:sequence; foreign conversation cursors fail. |

## POST /v1.0/api/participants

Create a reusable direct CLI or PolyPrompt completion profile. Access: any signed-in user of an active tenant.

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

List current runner profiles; excludes historical snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<RunnerProfile>`.

## PUT /v1.0/api/participants/{profileId}

Edit a reusable profile without changing historical run snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `RunnerProfile`. Request: `RunnerProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |

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

Read execution history and immutable participants. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DebateRun>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## POST /v1.0/api/conversations/{conversationId}/runs

Start a debate or single-participant follow-up with a durable receipt. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `DebateRun`. Request: `CreateRunRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

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

Read a durable checkpoint and current revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `DebateRun`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

## POST /v1.0/api/conversations/{conversationId}/runs/{runId}/actions

Apply pause, resume, interrupt, stop, continue, extend, note, steer, hold or release. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

Representative request:

```json
{
  "action": "stop",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v1.0/api/request-history

Inspect scoped request metadata with backend filtering and paging. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `HistoryPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Case-insensitive text to find in paths or titles. |
| method | query | false | HTTP method to match, for example GET or POST. |
| status | query | false | HTTP status code to match, for example 409. |
| fromUtc | query | false | Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z. |
| toUtc | query | false | End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v1.0/api/request-history/summary

Return zero-filled UTC activity buckets for a bounded range. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `List<ActivityBucket>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| fromUtc | query | true | Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z. |
| toUtc | query | true | End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z. |
| bucketMinutes | query | false | Fixed UTC interval in minutes. Dashboard presets: last hour 1 (60 buckets), last day 15 (96), last week 120 (84), rolling 30 days 480 (90). The range is [fromUtc, toUtc), limited to 31 days and 2000 buckets, including zero-count intervals. HTTP statuses below 400 count as success; 400 and above count as errors. Disabled request history returns an empty array. |

## GET /v1.0/api/request-history/{requestId}

Read redacted request and response headers and body prefixes. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `RequestHistoryEntry`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Request ID from request history. |

## DELETE /v1.0/api/request-history/{requestId}

Delete one authorized retained request capture. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Request ID from request history. |

## GET /v1.0/api/settings

Read redacted file settings, revision and restart metadata. Access: system administrators.

Success: **200**, response: `SettingsView`.

## PUT /v1.0/api/settings

Atomically update allowlisted nonsecret settings using its revision. Access: system administrators. May execute work or change service state; do not blindly replay requests.

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

Drain workers and reload settings; native hosts restart in-process, containers exit for their supervisor. Access: system administrators. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `RestartResponse`. Request: `object`.

Representative request:

```json
{}
```

## DELETE /v1.0/api/participants/{profileId}

Delete a reusable participant, preserving historical snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |

## DELETE /v1.0/api/conversations/{conversationId}

Permanently delete a stopped conversation and its retained messages/events. Access: any signed-in user of an active tenant.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## POST /v1.0/api/settings/shutdown

Drain active work and stop the service; a supervisor may restart it. Access: system administrators.

Success: **202**, response: `Acknowledgement`.

## GET /v1.0/api/conversations/{conversationId}/runs/{runId}/attempts

Page durable generation attempts with frozen participant, prompt and context provenance. Access: any signed-in user of an active tenant.

Success: **200**, response: `AttemptPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v1.0/api/conversations/{conversationId}/messages/{messageId}

Read one complete owned message without loading other history. Access: any signed-in user of an active tenant.

Success: **200**, response: `Message`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| messageId | path | true | Message ID (msg_...). Copy it from a message header. |

## POST /v2.0/api/conversations

v2: Create a persistent conversation without starting any model. Access: any signed-in user of an active tenant.

Success: **201**, response: `Conversation`. Request: `ConversationRequest`.

Representative request:

```json
{
  "title": "Example architecture decision"
}
```

## GET /v2.0/api/conversations

v2: Search owned titles and message content with exact totals. Access: any signed-in user of an active tenant.

Success: **200**, response: `Page`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Case-insensitive text to find in paths or titles. |
| archived | query | false | true lists archived conversations instead of active ones. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v2.0/api/conversations/{conversationId}

v2: Read an owned conversation. Access: any signed-in user of an active tenant.

Success: **200**, response: `Conversation`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## PUT /v2.0/api/conversations/{conversationId}

v2: Change title, pin or archive using an expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `Conversation`. Request: `ConversationRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "title": "Updated example decision",
  "pinned": true,
  "archived": false,
  "revision": "{{conversationRevision}}"
}
```

## GET /v2.0/api/conversations/{conversationId}/messages

v2: Read bounded owned message previews with stable membership and exact totals. Access: any signed-in user of an active tenant.

Success: **200**, response: `MessagePage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |
| throughSequence | query | false | Optional captured membership boundary. Omission captures the latest sequence. Streaming content itself is not frozen. Future boundaries are rejected. |
| tail | query | false | Select the last page of the captured membership, overriding pageNumber. Preview text is bounded; use individual message reads for full content. |

## POST /v2.0/api/conversations/{conversationId}/messages

v2: Append a public note with command deduplication. Access: any signed-in user of an active tenant.

Success: **201**, response: `Message`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "action": "note",
  "text": "Consider reversibility and ownership.",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v2.0/api/conversations/{conversationId}/events

v2: Read committed events after a cursor; Accept text/event-stream enables resumable SSE. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DurableEvent>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| after | query | false | Exclusive sequence cursor: return items after this sequence number. Use the last sequence you received. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |
| Accept | header | false | Protocol control. |
| Last-Event-ID | header | false | SSE cursor formatted conversationId:sequence; foreign conversation cursors fail. |

## POST /v2.0/api/participants

v2: Create a reusable direct CLI or PolyPrompt completion profile. Access: any signed-in user of an active tenant.

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

## GET /v2.0/api/participants

v2: List current runner profiles; excludes historical snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<RunnerProfile>`.

## PUT /v2.0/api/participants/{profileId}

v2: Edit a reusable profile without changing historical run snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `RunnerProfile`. Request: `RunnerProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |

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

## GET /v2.0/api/conversations/{conversationId}/runs

v2: Read execution history and immutable participants. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DebateRun>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## POST /v2.0/api/conversations/{conversationId}/runs

Start a model-guided panel with an explicit orchestrator. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `DebateRun`. Request: `CreateRunRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "protocolVersion": 2,
  "mode": "debate",
  "participantIds": [
    "{{profileId}}",
    "{{secondProfileId}}",
    "{{thirdProfileId}}",
    "{{fourthProfileId}}"
  ],
  "orchestratorId": "{{orchestratorId}}",
  "maxRounds": 3,
  "interventionSeconds": 5,
  "idempotencyKey": "{{commandKey}}"
}
```

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}

v2: Read a durable checkpoint and current revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `DebateRun`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

## POST /v2.0/api/conversations/{conversationId}/runs/{runId}/actions

v2: Apply pause, resume, interrupt, stop, continue, extend, edit_proposal, rebuild_summary, exclude_summary, hold or release. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`. Request: `CommandRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

Representative request:

```json
{
  "action": "stop",
  "idempotencyKey": "{{$guid}}"
}
```

## GET /v2.0/api/request-history

v2: Inspect scoped request metadata with backend filtering and paging. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `HistoryPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Case-insensitive text to find in paths or titles. |
| method | query | false | HTTP method to match, for example GET or POST. |
| status | query | false | HTTP status code to match, for example 409. |
| fromUtc | query | false | Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z. |
| toUtc | query | false | End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v2.0/api/request-history/summary

v2: Return zero-filled UTC activity buckets for a bounded range. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `List<ActivityBucket>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| fromUtc | query | true | Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z. |
| toUtc | query | true | End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z. |
| bucketMinutes | query | false | Fixed UTC interval in minutes. Dashboard presets: last hour 1 (60 buckets), last day 15 (96), last week 120 (84), rolling 30 days 480 (90). The range is [fromUtc, toUtc), limited to 31 days and 2000 buckets, including zero-count intervals. HTTP statuses below 400 count as success; 400 and above count as errors. Disabled request history returns an empty array. |

## GET /v2.0/api/request-history/{requestId}

v2: Read redacted request and response headers and body prefixes. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `RequestHistoryEntry`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Request ID from request history. |

## DELETE /v2.0/api/request-history/{requestId}

v2: Delete one authorized retained request capture. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| requestId | path | true | Request ID from request history. |

## GET /v2.0/api/settings

v2: Read redacted file settings, revision and restart metadata. Access: system administrators.

Success: **200**, response: `SettingsView`.

## PUT /v2.0/api/settings

v2: Atomically update allowlisted nonsecret settings using its revision. Access: system administrators. May execute work or change service state; do not blindly replay requests.

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

## POST /v2.0/api/settings/restart

v2: Drain workers and reload settings; native hosts restart in-process, containers exit for their supervisor. Access: system administrators. May execute work or change service state; do not blindly replay requests.

Success: **202**, response: `RestartResponse`. Request: `object`.

Representative request:

```json
{}
```

## DELETE /v2.0/api/participants/{profileId}

v2: Delete a reusable participant, preserving historical snapshots. Access: any signed-in user of an active tenant.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |

## DELETE /v2.0/api/conversations/{conversationId}

v2: Permanently delete a stopped conversation and its retained messages/events. Access: any signed-in user of an active tenant.

Success: **200**, response: `DeletionResponse`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## POST /v2.0/api/settings/shutdown

v2: Drain active work and stop the service; a supervisor may restart it. Access: system administrators.

Success: **202**, response: `Acknowledgement`.

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/attempts

v2: Page durable generation attempts with frozen participant, prompt and context provenance. Access: any signed-in user of an active tenant.

Success: **200**, response: `AttemptPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v2.0/api/conversations/{conversationId}/messages/{messageId}

v2: Read one complete owned message without loading other history. Access: any signed-in user of an active tenant.

Success: **200**, response: `Message`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| messageId | path | true | Message ID (msg_...). Copy it from a message header. |

## GET /v2.0/api/capabilities

Discover panel limits and unanimity policy. Access: any signed-in user of an active tenant.

Success: **200**, response: `ServiceCapabilities`.

## POST /v2.0/api/conversations/{conversationId}/interjections

Accept context with an explicit delivery mode. Access: any signed-in user of an active tenant.

Success: **201**, response: `InterjectionReceipt`. Request: `InterjectionRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "runId": "{{runId}}",
  "text": "Preserve reversibility during rollout.",
  "deliveryMode": "next_turn",
  "pinned": true,
  "idempotencyKey": "{{commandKey}}"
}
```

## GET /v2.0/api/conversations/{conversationId}/interjections

Read context receipts and exact participant deliveries. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<InterjectionReceipt>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| pageNumber | query | false | Page to return, starting at 1. |

## POST /v2.0/api/conversations/{conversationId}/runs/{runId}/roster

Queue a roster change with expected run revision. Access: any signed-in user of an active tenant.

Success: **202**, response: `Acknowledgement`. Request: `RosterChangeRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

Representative request:

```json
{
  "action": "add",
  "profileId": "{{replacementProfileId}}",
  "revision": 1,
  "idempotencyKey": "{{commandKey}}"
}
```

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/roster

Read current and historical panel seats. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DebateSeat>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/proposals

Inspect exact proposals and every recorded vote; paged newest first. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<DebateProposal>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |
| pageNumber | query | false | Page to return, starting at 1. |

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/summaries

Read shared and private summaries with paginated immutable source manifests. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ContextSummary>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |
| pageNumber | query | false | Page to return, starting at 1. |
| id | query | false | Restrict the result to this identifier. |
| sourcePageNumber | query | false | Page of original summary sources to return, 100 per page, starting at 1. |

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/clarifications

Read pending and answered moderator questions; paged newest first. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ClarificationRequest>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |
| pageNumber | query | false | Page to return, starting at 1. |

## GET /v2.0/api/configuration/endpoints

List endpoints without secret values; bounded pages default to 100 items. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<EndpointProfile>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## POST /v2.0/api/configuration/endpoints

Create endpoints configuration. Access: any signed-in user of an active tenant.

Success: **201**, response: `EndpointProfile`. Request: `EndpointProfile`.

Representative request:

```json
{
  "name": "Example gateway",
  "protocol": "openai",
  "baseUrl": "{{simulationEndpoint}}",
  "timeoutSeconds": 300,
  "enabled": true,
  "authBindings": [
    {
      "kind": "bearer",
      "credentialId": "{{credentialId}}"
    },
    {
      "kind": "header",
      "name": "X-Route",
      "value": "example"
    }
  ]
}
```

## PUT /v2.0/api/configuration/endpoints/{id}

Replace endpoints configuration with expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `EndpointProfile`. Request: `EndpointProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Example gateway",
  "protocol": "openai",
  "baseUrl": "{{simulationEndpoint}}",
  "timeoutSeconds": 300,
  "enabled": true,
  "authBindings": [
    {
      "kind": "bearer",
      "credentialId": "{{credentialId}}"
    },
    {
      "kind": "header",
      "name": "X-Route",
      "value": "example"
    }
  ],
  "revision": 1
}
```

## GET /v2.0/api/configuration/harnesses

List harnesses without secret values; bounded pages default to 100 items. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<HarnessProfile>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## POST /v2.0/api/configuration/harnesses

Create harnesses configuration. Access: any signed-in user of an active tenant.

Success: **201**, response: `HarnessProfile`. Request: `HarnessProfile`.

Representative request:

```json
{
  "name": "Claude Code",
  "provider": "claude",
  "executable": "claude",
  "arguments": [
    "-p",
    "--verbose",
    "--output-format",
    "stream-json",
    "--dangerously-skip-permissions"
  ],
  "credentialId": "{{credentialId}}",
  "enabled": true
}
```

## PUT /v2.0/api/configuration/harnesses/{id}

Replace harnesses configuration with expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `HarnessProfile`. Request: `HarnessProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Claude Code",
  "provider": "claude",
  "executable": "claude",
  "arguments": [
    "-p",
    "--verbose",
    "--output-format",
    "stream-json",
    "--dangerously-skip-permissions"
  ],
  "credentialId": "{{credentialId}}",
  "enabled": true,
  "revision": 1
}
```

## GET /v2.0/api/configuration/models

List models without secret values; bounded pages default to 100 items. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ModelProfile>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## POST /v2.0/api/configuration/models

Create models configuration. Access: any signed-in user of an active tenant.

Success: **201**, response: `ModelProfile`. Request: `ModelProfile`.

Representative request:

```json
{
  "name": "Example model",
  "endpointId": "{{endpointId}}",
  "model": "multi-agent",
  "contextMaxCharacters": 262144,
  "supportsTools": true,
  "enabled": true
}
```

## PUT /v2.0/api/configuration/models/{id}

Replace models configuration with expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `ModelProfile`. Request: `ModelProfile`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Example model",
  "endpointId": "{{endpointId}}",
  "model": "multi-agent",
  "contextMaxCharacters": 262144,
  "supportsTools": true,
  "enabled": true,
  "revision": 1
}
```

## GET /v2.0/api/configuration/secrets

List secrets without secret values; bounded pages default to 100 items. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<CredentialMetadata>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v2.0/api/configuration/credentials

Deprecated alias of GET /v2.0/api/configuration/secrets. Access: any signed-in user of an active tenant. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **200**, response: `List<CredentialMetadata>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## POST /v2.0/api/configuration/secrets

Create secrets configuration. Access: any signed-in user of an active tenant.

Success: **201**, response: `CredentialMetadata`. Request: `CredentialRequest`.

Representative request:

```json
{
  "name": "Example gateway credential",
  "kind": "http_header",
  "parameterName": "x-api-key",
  "secretValue": "{{credentialValue}}"
}
```

## POST /v2.0/api/configuration/credentials

Deprecated alias of POST /v2.0/api/configuration/secrets. Access: any signed-in user of an active tenant. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **201**, response: `CredentialMetadata`. Request: `CredentialRequest`.

Representative request:

```json
{
  "name": "Example gateway credential",
  "kind": "http_header",
  "parameterName": "x-api-key",
  "secretValue": "{{credentialValue}}"
}
```

## PUT /v2.0/api/configuration/secrets/{id}

Replace secrets configuration with expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `CredentialMetadata`. Request: `CredentialRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Example gateway credential",
  "kind": "token",
  "secretValue": "{{credentialValue}}",
  "revision": 1
}
```

## PUT /v2.0/api/configuration/credentials/{id}

Deprecated alias of PUT /v2.0/api/configuration/secrets/{id}. Access: any signed-in user of an active tenant. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **200**, response: `CredentialMetadata`. Request: `CredentialRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Example gateway credential",
  "kind": "token",
  "secretValue": "{{credentialValue}}",
  "revision": 1
}
```

## POST /v2.0/api/configuration/secrets/{id}/reveal

Reveal a ready secret's decrypted value to the moderator. Access: any signed-in user of an active tenant.

Success: **200**, response: `RevealedCredential`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## POST /v2.0/api/configuration/credentials/{id}/reveal

Deprecated alias of POST /v2.0/api/configuration/secrets/{id}/reveal. Access: any signed-in user of an active tenant. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **200**, response: `RevealedCredential`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## DELETE /v2.0/api/configuration/secrets/{id}

Disable a secret while keeping historical references. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |
| revision | query | false | Expected current revision; the request is refused if the resource changed. |

## DELETE /v2.0/api/configuration/credentials/{id}

Deprecated alias of DELETE /v2.0/api/configuration/secrets/{id}. Access: any signed-in user of an active tenant. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |
| revision | query | false | Expected current revision; the request is refused if the resource changed. |

## GET /v2.0/api/prompt-parameters

List prompt placeholders and the debate steps where each has a value. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `List<PromptParameter>`.

## GET /v2.0/api/prompts

List factory and workspace prompts. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `List<PromptTemplate>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| purpose | query | false | Backend filter. |

## POST /v2.0/api/prompts

Create a workspace prompt. Access: any signed-in user of an active tenant.

Success: **201**, response: `PromptTemplate`. Request: `PromptTemplate`.

Representative request:

```json
{
  "name": "Skeptical question",
  "purpose": "question",
  "description": "Pushes on the weakest assumption.",
  "body": "You are {agent_name}, the orchestrator. Debate question: {question}\nPanel: {panel}. Round {round} of {max_rounds}.\n{guidance}\nAsk the single question most likely to expose a weak assumption. Return only a JSON object with question and rationale."
}
```

## GET /v2.0/api/prompts/{id}

Read a prompt. Access: any signed-in user of an active tenant.

Success: **200**, response: `PromptTemplate`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## PUT /v2.0/api/prompts/{id}

Update a workspace prompt with its expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `PromptTemplate`. Request: `PromptTemplate`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "name": "Skeptical question",
  "description": "Pushes on the weakest assumption.",
  "body": "You are {agent_name}. Question: {question}\n{guidance}\nReturn only a JSON object with question and rationale.",
  "revision": 1
}
```

## POST /v2.0/api/prompts/{id}/duplicate

Copy a prompt, including a factory default, into an editable workspace prompt. Access: any signed-in user of an active tenant.

Success: **201**, response: `PromptTemplate`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## DELETE /v2.0/api/prompts/{id}

Delete a workspace prompt; factory prompts cannot be deleted. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |
| revision | query | false | Expected current revision; the request is refused if the resource changed. |

## POST /v2.0/api/configuration/endpoints/{id}/discover

Check endpoint reachability and list advertised models without invoking a completion; sends configured credentials. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `EndpointDiscovery`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## GET /v2.0/api/configuration/secret-keys

Read safe master-key identities. Access: system administrators.

Success: **200**, response: `CredentialKeyStatus`.

## GET /v2.0/api/configuration/credential-keys

Deprecated alias of GET /v2.0/api/configuration/secret-keys. Access: system administrators. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x.

Success: **200**, response: `CredentialKeyStatus`.

## POST /v2.0/api/configuration/secret-keys/rotate

Rotate the expected master key and reencrypt secrets. Access: system administrators. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `CredentialKeyStatus`. Request: `RotateCredentialKeyRequest`.

Representative request:

```json
{
  "expectedKeyId": "{{activeKeyId}}"
}
```

## POST /v2.0/api/configuration/credential-keys/rotate

Deprecated alias of POST /v2.0/api/configuration/secret-keys/rotate. Access: system administrators. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `CredentialKeyStatus`. Request: `RotateCredentialKeyRequest`.

Representative request:

```json
{
  "expectedKeyId": "{{activeKeyId}}"
}
```

## POST /v2.0/api/configuration/secret-keys/retire

Retire earlier master keys once no secret uses them. Access: system administrators. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `CredentialKeyStatus`. Request: `RotateCredentialKeyRequest`.

Representative request:

```json
{
  "expectedKeyId": "{{activeKeyId}}"
}
```

## POST /v2.0/api/configuration/credential-keys/retire

Deprecated alias of POST /v2.0/api/configuration/secret-keys/retire. Access: system administrators. Deprecated: use the /configuration/secrets or /configuration/secret-keys route instead; this alias is removed after 2.1.x. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `CredentialKeyStatus`. Request: `RotateCredentialKeyRequest`.

Representative request:

```json
{
  "expectedKeyId": "{{activeKeyId}}"
}
```

## GET /v2.0/api/configuration/models/{id}/checks

Read observed completion/tool checks. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ModelCheck>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |
| pageNumber | query | false | Page to return, starting at 1. |

## POST /v2.0/api/configuration/models/{id}/checks

Explicitly invoke a potentially billable model check. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `ModelCheck`. Request: `ModelCheckRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

Representative request:

```json
{
  "idempotencyKey": "{{commandKey}}",
  "checkTools": false
}
```

## GET /v2.0/api/configuration/endpoints/{id}/preview

Preview normalized destinations and binding names without contacting the endpoint or resolving credentials. Access: any signed-in user of an active tenant.

Success: **200**, response: `EndpointPreview`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| id | path | true | Resource ID. Copy it from the matching list. |

## POST /v2.0/api/token

Sign in with an email and password and receive a session token. Access: public; no bearer token.

Success: **201**, response: `TokenResponse`. Request: `LoginRequest`.

Representative request:

```json
{
  "email": "admin@arguebot",
  "password": "password",
  "tenantId": ""
}
```

## GET /v2.0/api/login-info

Read sign-in page information, including the default credentials. Access: public; no bearer token.

Success: **200**, response: `LoginInfo`.

## GET /v2.0/api/token

Check that the bearer token is valid. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

## GET /v2.0/api/token/details

Describe the bearer token: user, tenant, principal and expiry. Access: any signed-in user of an active tenant.

Success: **200**, response: `TokenDetails`.

## POST /v2.0/api/token/refresh

Replace the session token with one that has a fresh lifetime. Access: any signed-in user of an active tenant.

Success: **200**, response: `TokenResponse`.

## DELETE /v2.0/api/token

Sign out by revoking the current session token. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`.

## GET /v2.0/api/me

Read the signed-in user. Access: any signed-in user of an active tenant.

Success: **200**, response: `User`.

## PUT /v2.0/api/me

Change your name or email with the expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `User`. Request: `UserRequest`.

Representative request:

```json
{
  "firstName": "Default",
  "lastName": "Admin",
  "email": "admin@arguebot",
  "revision": "{{userRevision}}"
}
```

## PUT /v2.0/api/me/password

Change your password and sign out your other sessions. Access: any signed-in user of an active tenant. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`. Request: `PasswordChangeRequest`.

Representative request:

```json
{
  "currentPassword": "{{currentPassword}}",
  "newPassword": "{{newPassword}}"
}
```

## GET /v2.0/api/me/credentials

List your bearer-token credentials. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<AuthCredential>`.

## POST /v2.0/api/me/credentials

Create a bearer-token credential; the token is returned only once. Access: any signed-in user of an active tenant.

Success: **201**, response: `AuthCredentialCreated`. Request: `AuthCredentialRequest`.

Representative request:

```json
{
  "name": "Fictional automation",
  "expiresUtc": null,
  "active": true,
  "revision": 0
}
```

## PUT /v2.0/api/me/credentials/{credentialId}

Rename, enable or disable one of your credentials. Access: any signed-in user of an active tenant.

Success: **200**, response: `AuthCredential`. Request: `AuthCredentialRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

Representative request:

```json
{
  "name": "Fictional automation",
  "active": false,
  "revision": "{{resourceRevision}}"
}
```

## DELETE /v2.0/api/me/credentials/{credentialId}

Delete one of your credentials. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

## GET /v2.0/api/me/sessions

List your active sessions. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<AuthSession>`.

## DELETE /v2.0/api/me/sessions/{sessionId}

Sign out one of your sessions. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| sessionId | path | true | Session ID (ses_...). Copy it from the session list. |

## GET /v2.0/api/users

List users in your tenant. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `List<User>`.

## POST /v2.0/api/users

Create a user in your tenant. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **201**, response: `User`. Request: `UserRequest`.

Representative request:

```json
{
  "firstName": "Fictional",
  "lastName": "Member",
  "email": "member@example.test",
  "password": "{{newPassword}}",
  "isTenantAdmin": false,
  "active": true
}
```

## GET /v2.0/api/users/{userId}

Read a user. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `User`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

## PUT /v2.0/api/users/{userId}

Update a user with the expected revision. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `User`. Request: `UserRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

Representative request:

```json
{
  "firstName": "Fictional",
  "lastName": "Member",
  "email": "member@example.test",
  "isTenantAdmin": false,
  "active": true,
  "revision": "{{resourceRevision}}"
}
```

## DELETE /v2.0/api/users/{userId}

Delete a user; their conversations and agents stay in the tenant. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

## PUT /v2.0/api/users/{userId}/password

Reset a user's password and sign out their sessions. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`. Request: `PasswordChangeRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

Representative request:

```json
{
  "newPassword": "{{newPassword}}"
}
```

## GET /v2.0/api/users/{userId}/credentials

List a user's credentials. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `List<AuthCredential>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

## POST /v2.0/api/users/{userId}/credentials

Create a credential for a user; the token is returned only once. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **201**, response: `AuthCredentialCreated`. Request: `AuthCredentialRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

Representative request:

```json
{
  "name": "Fictional integration",
  "active": true,
  "revision": 0
}
```

## PUT /v2.0/api/users/{userId}/credentials/{credentialId}

Rename, enable or disable a user's credential. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `AuthCredential`. Request: `AuthCredentialRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

Representative request:

```json
{
  "name": "Fictional integration",
  "active": false,
  "revision": "{{resourceRevision}}"
}
```

## DELETE /v2.0/api/users/{userId}/credentials/{credentialId}

Delete a user's credential. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

## GET /v2.0/api/users/{userId}/sessions

List a user's active sessions. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `List<AuthSession>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |

## DELETE /v2.0/api/sessions/{sessionId}

Revoke any session in your tenant. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| sessionId | path | true | Session ID (ses_...). Copy it from the session list. |

## GET /v2.0/api/audit

Search the security audit log, newest first. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `AuditPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| operation | query | false | Exact audit event type, for example AuthFailure, AuthorizationDenied, SessionIssued or UserCreated. |
| outcome | query | false | success, failure or denied. |
| userId | query | false | User ID (usr_...). |
| tenantId | query | false | Tenant ID (ten_...). System administrators only; tenant administrators always see their own tenant. |
| fromUtc | query | false | Start of the range, UTC ISO 8601, for example 2026-01-01T00:00:00Z. |
| toUtc | query | false | End of the range, UTC ISO 8601, for example 2026-01-02T00:00:00Z. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## DELETE /v2.0/api/audit

Delete audit records older than beforeUtc. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `DeletedCount`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| beforeUtc | query | false | Delete records created before this UTC ISO 8601 time. |

## GET /v2.0/api/tenants

List tenants. Access: system administrators.

Success: **200**, response: `List<Tenant>`.

## POST /v2.0/api/tenants

Create a tenant. Access: system administrators.

Success: **201**, response: `Tenant`. Request: `TenantRequest`.

Representative request:

```json
{
  "name": "Fictional tenant",
  "active": true
}
```

## GET /v2.0/api/tenants/{tenantId}

Read a tenant. Access: system administrators.

Success: **200**, response: `Tenant`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| tenantId | path | true | Tenant ID (ten_...). Copy it from Administration > Tenants. |

## PUT /v2.0/api/tenants/{tenantId}

Rename, enable or disable a tenant with the expected revision. Access: system administrators.

Success: **200**, response: `Tenant`. Request: `TenantRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| tenantId | path | true | Tenant ID (ten_...). Copy it from Administration > Tenants. |

Representative request:

```json
{
  "name": "Fictional tenant",
  "active": true,
  "revision": "{{resourceRevision}}"
}
```

## DELETE /v2.0/api/tenants/{tenantId}

Delete a tenant and everything in it; confirmName must be its exact name. Access: system administrators. May execute work or change service state; do not blindly replay requests.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| tenantId | path | true | Tenant ID (ten_...). Copy it from Administration > Tenants. |
| confirmName | query | false | The tenant's exact current name, to confirm deletion. |

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
| field | string | Request field to correct, in camel case, or empty when the failure is not field-specific. |

### AttemptPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching durable attempts. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matching records. |

### AuditEvent

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Audit record ID. |
| tenantId | string | Tenant, when resolvable. |
| userId | string | User, when resolvable. |
| operation | string | Event type, for example AuthSuccess, AuthFailure, SessionIssued, SessionRevoked, CredentialUsed or AuthorizationDenied. |
| outcome | string | success, failure or denied. |
| principalType | string | User or Credential. |
| sessionId | string | Session, when used or created. |
| credentialId | string | Credential, when used. |
| authScheme | string | Authentication scheme. |
| correlationId | string | Request ID. |
| requestType | string | Classified request type. |
| method | string | HTTP method. |
| path | string | URL path without the query. |
| sourceIp | string | Client address. |
| authenticationResult | string | Success, NotFound, Inactive or Invalid. |
| authorizationResult | string | Permitted, DeniedImplicit or NotFound. |
| reason | string | Free-text reason. |
| resourceType | string | Affected resource type. |
| resourceId | string | Affected resource ID. |
| errorCode | string | Error code returned. |
| statusCode | integer | HTTP status returned. |
| createdUtc | string | When the event happened. |

### AuditPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Records on this page. |
| pageNumber | integer | One-based page number. |
| pageSize | integer | Records per page, 1 through 200. |
| totalCount | integer | Records matching the filter. |

### AuthCredential

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Credential identifier. |
| tenantId | string | Owning tenant. |
| userId | string | Owning user. |
| name | string | Display name. |
| tokenLast4 | string | Last four characters of the token, for recognition. |
| authMode | string | Authentication mode; always DirectHeader (bearer token). |
| active | boolean | False rejects the token. |
| protected | boolean | Protected credentials cannot be deleted or disabled. |
| expiresUtc | string (nullable) | Optional expiry in UTC. |
| lastUsedUtc | string (nullable) | Last time the token authenticated a request. |
| revision | integer | Optimistic revision. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### AuthCredentialCreated

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| credential | AuthCredential | The stored credential. |
| token | string | The bearer token. Store it now; it cannot be retrieved later. |

### AuthCredentialRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Display name, 1 through 200 characters. |
| expiresUtc | string (nullable) | Optional expiry in UTC; must be in the future when creating. |
| active | boolean | False rejects the token. |
| revision | integer | Expected revision when updating. |

### AuthSession

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Session ID. |
| tenantId | string | Tenant the session is bound to. |
| userId | string | Signed-in user. |
| credentialId | string | Credential that created the session, or empty for a password sign-in. |
| principalType | string | User or Credential. |
| authScheme | string | How the session was created, for example PasswordHeaders or PasswordBody. |
| sourceIp | string | Address that created the session. |
| userAgent | string | Client that created the session. |
| createdUtc | string | When the session was issued. |
| expiresUtc | string | When the session stops working unless refreshed. |
| lastUsedUtc | string (nullable) | Last authenticated request, updated at most once a minute. |
| revokedUtc | string (nullable) | When the session was revoked. |
| revocationReason | string | Why the session was revoked. |
| active | boolean | False once revoked. |
| current | boolean | True for the session that made this request. |

### AuthorizationError

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| code | string | Machine-readable error code, for example forbidden or tenant_mismatch. |
| message | string | Human-readable explanation. |
| reason | string | Short reason for the denial. |
| requiredPermission | string | Access class the route needs: Member, TenantAdmin or SystemAdmin. |
| requestType | string | Classified request, for example GET /v2.0/api/users. |
| tenantId | string | Tenant of the caller. |
| requestId | string | Correlation ID for request history and audit. |

### CapturedHeader

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Header name. |
| value | string | Redacted header value. |
| response | boolean | Whether this is a response header. |

### ClarificationRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Request ID. |
| participantId | string | Originating seat. |
| question | string | Question the moderator should answer. |
| blocking | boolean | Whether execution pauses for the answer. |
| status | string | open, answered or skipped. |

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

### ContextSummary

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable summary identifier. |
| messageId | string | Original model response containing the summary. |
| attemptId | string | Generation attempt with the model snapshot and prompt digest. |
| runId | string | Containing run. |
| content | string | Summary text; original turns remain authoritative. |
| throughSequence | integer | Last original conversation sequence covered. |
| contextEpoch | integer | Context epoch at generation acceptance. |
| active | boolean | Whether this summary is eligible for prompt selection. |
| recipientId | string | Private recipient snapshot ID; empty denotes shared memory. |
| sourceCount | integer | Total original source identities in this immutable manifest. |
| sourcePageNumber | integer | One-based page of source identities returned, 100 per page. |
| sourceIds | array | One bounded page of original source IDs, retained through successive compactions. |
| sources | array | Content hashes for the same bounded page as SourceIds; hashes establish provenance, not semantic accuracy. |
| parentSummaryIds | array | Prior summaries selected for this compaction, ordered by the invocation manifest; originals remain in Sources. |
| sections | array | Seven validated memory categories, also rendered in Content; empty for historical unstructured summaries. Validation does not prove semantic accuracy. |

### Conversation

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable conversation identifier. |
| lastEventSequence | integer | Latest committed event sequence observed before this conversation read. Capture it before loading snapshots, then replay after it to cover concurrent changes. |
| title | string | Display title. |
| openingQuestion | string | The question a debate in this conversation starts from. Blank means the title. A started debate freezes its own copy. |
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
| openingQuestion | string (nullable) | The question a debate in this conversation starts from; blank means the title. On update, null keeps the stored question. |
| pinned | boolean | Pinned state. |
| archived | boolean | Archived state. |
| revision | integer (nullable) | Expected revision. |

### CreateRunRequest

Protocol 2 on /v2.0 requires an explicit orchestratorId and 2 through the advertised maximum participant IDs; consensus requires every active seat to agree on one proposal. Protocol 1 retains two or three distinct participant IDs and accepts a question up to 4000 characters; blank uses the conversation title. Followup requires one participant and a nonempty question, and forces one round with no countdown.

| Field | Type | Meaning |
| --- | --- | --- |
| policy | DebatePolicy | Frozen execution limits and orchestration instructions for protocol 2. |
| mode | string | Run purpose: debate or followup. Follow-up uses exactly one participant. |
| question | string | Debate question frozen for this run; blank uses the conversation's opening question, or its title when that is blank. Follow-up questions are persisted as messages. |
| participantIds | array | Ordered profiles. |
| maxRounds | integer | Round limit. |
| interventionSeconds | integer | Intervention countdown. |
| idempotencyKey | string | Unique command key. |
| protocolVersion | integer | Debate protocol: 1 for historical compatibility, 2 for model-guided unanimous debates. |
| orchestratorId | string | Reusable orchestrator profile, required for protocol 2 debates. |

### CredentialKeyStatus

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| activeKeyId | string | Active master key identity; use as the expected identity for rotation. |
| retainedKeyCount | integer | Number of keys retained to read existing records and older backups. |
| reencryptedCredentials | integer | Credential records reencrypted by this operation; zero on a status read. |
| skippedCredentials | integer | Disabled credentials that could not be decrypted and were left unchanged by this rotation; replace or ignore them. |
| retiredKeyCount | integer | Earlier keys removed by this operation; zero unless keys were retired. |

### CredentialMetadata

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable write-only credential reference. |
| name | string | Safe display name. |
| kind | string | token, claude_oauth or codex_auth_json. |
| parameterName | string | Header or query parameter name for http_header and http_query credentials; empty for every other kind. Fixed after creation. |
| status | string | ready; disabled; or refresh_uncertain when a Codex token refresh was sent but its result is unknown, so the stored refresh token must not be replayed and the login must be replaced. |
| revision | integer | Current credential revision; replacement requires this value. |

### CredentialRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Safe display name. |
| kind | string | token, claude_oauth or codex_auth_json. |
| parameterName | string | Header or query parameter name for http_header and http_query credentials; empty for every other kind. Fixed after creation. |
| secretValue | string | Write-only secret; never returned or recorded in request history. |
| revision | integer | Expected revision for replacement. |

### CredentialRevision

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| credentialId | string | Stable credential identity without its value. |
| revision | integer | Revision read atomically with the encrypted value. |

### DebateAgreement

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| participantId | string | Stable participant snapshot ID. |
| messageId | string | Original ratification response. |
| position | string | Explicit agreement, disagreement, abstention or information request. |
| rationale | string | Public explanation. |
| superseded | boolean | True when the same seat later voted again on this proposal; only the newest vote counts toward unanimity. |

### DebatePolicy

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| maxParticipantTurns | integer | Participant response and ratification invocation limit, including failed attempts. |
| maxOrchestrationCalls | integer | Question and synthesis invocation limit, including failed attempts. |
| maxCompactionCalls | integer | Summary invocation limit, including failed attempts. |
| maxToolCalls | integer | Conversation and local tool call limit for this run. |
| maxWallClockSeconds | integer | Elapsed deadline from run admission, including queues, intervention windows and pauses. |
| maxContextCharacters | integer | Per-invocation character ceiling, further restricted by service and model capacity. |
| maxEstimatedInputTokens | integer | Optional cumulative estimated initial-input token ceiling; zero disables it. This is an estimate, not provider accounting. |
| questionInstructions | string | Additional neutral question-selection guidance, up to 4000 characters. |
| synthesisInstructions | string | Additional proposal presentation guidance, up to 4000 characters. |
| summaryInstructions | string | Additional evidence-preservation guidance, up to 4000 characters. |

### DebateProposal

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable proposal identifier. |
| runId | string | Containing run. |
| messageId | string | Original synthesis message. |
| content | string | Exact candidate text. |
| digest | string | SHA-256 digest of the candidate. |
| contextEpoch | integer | Context revision at proposal acceptance. |
| rosterRevision | integer | Roster revision at proposal acceptance. |
| superseded | boolean | True when a moderator edit replaced this proposal. Its votes stay as history and never count toward the replacement. |
| moderatorEdited | boolean | True when the moderator wrote this revision instead of the orchestrator. |
| supersedesId | string | The proposal this moderator revision replaced, or empty for an orchestrator proposal. |
| agreements | array | Explicit positions on this proposal. |

### DebateRun

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| policy | DebatePolicy | Frozen execution limits and orchestration instructions for protocol 2. |
| usage | DebateUsage | Durable per-purpose invocation and tool reservations. |
| question | string | Debate question frozen when this run was created; follow-up runs retain the original conversation title. Renaming the conversation never changes historical prompts. Pre-schema-9 runs use the title available at migration. |
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
| protocolVersion | integer | Persisted debate protocol, independent of API and product versions. |
| phase | string | Current question, response, synthesis, ratification or compaction phase. |
| contextEpoch | integer | Current moderator-context revision. |
| rosterRevision | integer | Current active roster revision. |
| orchestrator | RunnerProfile (nullable) | Immutable orchestrator configuration snapshot. |
| proposalId | string | Current candidate proposal identifier. |
| currentQuestion | string | Current pointed question from the orchestrator. |
| prompts | array | Prompt text this debate froze for each step at start. Empty for runs created before prompts were stored; those use the factory text. |
| proposalText | string | Current ratification candidate text. |
| explicitMode | string | Original run purpose, retained independently of roster changes. |
| resumePhase | string | Phase resumed after successful context compaction. |
| compactionRecipientId | string | Private recipient partition for compaction; empty means shared evidence. |
| rebuildFromOriginals | boolean | True while a moderator-requested rebuild compacts original evidence; the prior shared summary stays active until the replacement validates. |
| repairPending | boolean | True when the previous orchestration step returned invalid structured output and the next attempt is its single repair request. |

### DebateSeat

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| participantId | string | Immutable runner snapshot ID. |
| name | string | Snapshot display name. |
| position | integer | Ordered seat position. |
| active | boolean | Whether the seat currently participates. |
| joinedRevision | integer | Roster revision where the seat joined. |
| leftRevision | integer (nullable) | Roster revision where the seat left, if any. |

### DebateUsage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| participantTurns | integer | Reserved participant turns for this run. |
| orchestrationCalls | integer | Reserved orchestration calls for this run. |
| compactionCalls | integer | Reserved compaction calls for this run. |
| toolCalls | integer | Reserved tool calls for this run. |
| estimatedInputTokens | integer | Reserved estimated input tokens for this run. |
| tokenEstimateMethod | string | Initial prompt UTF-8 bytes plus envelope allowance; not billed provider tokens or continuation accounting. |

### DeletedCount

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| deleted | integer | Records deleted. |

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

### EndpointDiscovery

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| endpointId | string | Checked endpoint identifier. |
| revision | integer | Configuration revision checked. |
| checkedUtc | string | Time the endpoint successfully returned its model list. |
| models | array | Distinct advertised model IDs, at most 1000; manual IDs remain supported. |
| truncated | boolean | Whether more advertised IDs were omitted from this bounded result. |

### EndpointPreview

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| endpointId | string | Saved endpoint identity. |
| revision | integer | Configuration revision described by this preview. |
| generationUrl | string | Normalized destination for POST generation requests, before query bindings are added. |
| discoveryUrl | string | Normalized destination for GET discovery requests, before query bindings are added. |
| headerNames | array | Configured outbound header names; every value is omitted, including nonsecret routing values. |
| queryNames | array | Configured query parameter names; credentials are neither resolved nor returned. |

### EndpointProfile

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable endpoint ID. |
| name | string | Display name. |
| protocol | string | ollama or openai HTTP protocol. |
| baseUrl | string | HTTP base URL without embedded credentials, query or fragment. |
| timeoutSeconds | integer | Generation deadline, 1 through 3600 seconds. |
| maxConcurrentInvocations | integer | Shared endpoint generation capacity, 1 through 64; changes affect subsequent admissions. |
| enabled | boolean | Whether new invocations may use this connection. |
| revision | integer | Expected revision when updating. |
| authBindings | array | Ordered HTTP authentication and routing bindings. |

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
| errorCode | string | Safe failure code without upstream response content. Per-turn deadlines use generation_timeout. Explicit interrupt, stop and orderly service shutdown use generation_interrupted, generation_stopped and service_shutdown; older interrupted attempts may have no code. |
| errorMessage | string | Safe cause and recovery guidance, without raw upstream payloads; historical diagnostics may refine the explanation. |
| promptVersion | string | Version of the deterministic system prompt template. |
| promptDigest | string | SHA-256 of the exact UTF-8 system prompt. |
| contextCharacters | integer | Exact UTF-16 character count of system prompt plus selected message content; this is not a token estimate. |
| estimatedInputTokens | integer | Conservative initial-input token estimate from UTF-8 bytes plus message framing, not billed usage. |
| durationMilliseconds | integer | Monotonic elapsed generation and durable stream flush time, zero if unavailable after a crash. |
| createdUtc | string | Acceptance time in UTC. |
| updatedUtc | string | Last lifecycle change in UTC. |
| contextMessageIds | array | Exact ordered source messages selected for this attempt, including any one-recipient directives. |
| purpose | string | response, question, synthesis, ratification or compaction. |
| repair | boolean | True when this attempt repeated a question, proposal or vote after invalid structured output. At most one repair follows each invalid answer. |
| contextEpoch | integer | Moderator context frozen before invocation. |
| rosterRevision | integer | Roster revision frozen before invocation. |
| endpointWaitMilliseconds | integer | Elapsed milliseconds waiting for shared endpoint capacity; zero for immediate admission, CLI or older records. |
| credentialRevisions | array | Credential identities and revisions actually resolved for this invocation; never credential values. |

### HarnessProfile

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable harness ID. |
| name | string | Display name. |
| provider | string | CLI output protocol. |
| executable | string | Direct executable or path on the execution host. |
| workingDirectory | string | Execution directory on the service host. |
| credentialId | string | Claude OAuth token or Codex auth.json credential reference. |
| enabled | boolean | Whether new runs may use this harness. |
| revision | integer | Expected revision when updating. |
| arguments | array | Literal ordered argument vector; prompts use standard input. |
| environmentVariables | array | Additional service environment variable names to inherit. Values are never stored or returned. |

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

### HttpAuthBinding

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| kind | string | Authentication kind: bearer, api_key, header or query. |
| name | string | Header or query parameter name; bearer uses Authorization. |
| value | string | Nonsecret static routing value; secret values use a credential reference. |
| credentialId | string | Write-only credential reference. |
| environmentVariable | string | Service environment reference, mutually exclusive with value or credential. |

### InterjectionReceipt

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Receipt identifier. |
| messageId | string | Original moderator message. |
| runId | string | Target run if specified. |
| deliveryMode | string | The moderator-selected mode. |
| contextEpoch | integer | Epoch after acceptance. |
| status | string | queued, interrupt_requested, applied, summarized or post_run. Delivery counts distinguish original context from derived memory. |
| pinned | boolean | Whether the original constraint is pinned. |
| deliveredParticipantIds | array | Seats whose successful invocation included this original context. |
| summarizedParticipantIds | array | Seats whose successful invocation selected a summary citing this source. This does not prove faithful preservation or include those already listed under direct delivery. |

### InterjectionRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| text | string | Context supplied by the moderator. |
| deliveryMode | string | Required: interrupt_now or next_turn. |
| recipientId | string | Optional active seat ID; empty shares context with the panel. |
| runId | string | Optional target run; empty adds post-run conversation context. |
| idempotencyKey | string | Unique command key; exact retries return the same receipt. |
| pinned | boolean | Keep this constraint in subsequent model context. |
| clarificationId | string | Optional clarification being answered or explicitly skipped. |
| skipClarification | boolean | Record an explicit skip instead of treating the text as a supplied fact. |

### LoginInfo

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| version | string | Product version. |
| defaultEmail | string | Default administrator email. |
| defaultPassword | string | Default administrator password. |
| defaultToken | string | Default administrator bearer token. |
| multipleTenants | boolean | True when more than one active tenant exists, so the page may need to ask for one. |

### LoginRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| email | string | Account email. |
| password | string | Account password. |
| tenantId | string | Tenant to sign in to; needed only when the email exists in several tenants. |

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

### ModelCheck

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable check identity. |
| modelId | string | Reusable model identity. |
| modelRevision | integer | Frozen model revision. |
| transportId | string | Frozen endpoint or harness identity. |
| transportRevision | integer | Frozen endpoint or harness revision. |
| state | string | queued, running, completed, failed, cancelled or uncertain. |
| checkTools | boolean | Whether the explicit request included a tool probe. |
| completionVerified | boolean | A complete nonempty response was observed. This does not certify debate quality. |
| toolsVerified | boolean | The exact harmless tool probe was received and answered. |
| errorCode | string | Safe failure classification; provider bodies and diagnostics are excluded. |
| executableVersion | string | Numeric version reported by a successful known CLI executable check; empty means unverified. |
| durationMilliseconds | integer | Monotonic elapsed check duration, including endpoint queueing. |
| createdUtc | string | Acceptance time in UTC. |
| updatedUtc | string | Last recorded state transition in UTC. |

### ModelCheckRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| idempotencyKey | string | Stable command identity. Reusing it returns the recorded check without invoking again. |
| checkTools | boolean | Ask the model to perform one harmless advertised tool round trip. |

### ModelGenerationSettings

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| maxOutputTokens | integer (nullable) | Requested generated-token ceiling, 1 through 1048576, independently enforced by the provider. Null retains the adapter default. |
| temperature | number (nullable) | Sampling temperature from zero through two, or null to omit the override. |
| topP | number (nullable) | Nucleus sampling probability from zero through one, or null to omit the override. |

### ModelProfile

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable reusable model ID. |
| name | string | Display name. |
| endpointId | string | REST endpoint reference, mutually exclusive with harness. |
| harnessId | string | CLI harness reference, mutually exclusive with endpoint. |
| model | string | Provider model identifier. |
| contextMaxCharacters | integer | Conservative UTF-16 character budget for this model; not a token count. |
| maxOutputBytes | integer | Service output capture ceiling, 1024 through 67108864 bytes, for CLI and HTTP models. |
| generation | ModelGenerationSettings | Optional HTTP sampling and generated-token overrides; configure CLI-specific generation flags in the harness instead. |
| supportsTools | boolean | Whether tools are declared available; supported CLI harnesses use native MCP and others use bounded envelopes. |
| enabled | boolean | Whether new runs may use this model. |
| revision | integer | Expected revision when updating. |

### Page

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Matching conversations. |
| pageNumber | integer | One-based page. |
| pageSize | integer | Page size. |
| totalCount | integer | Total matches. |

### PasswordChangeRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| currentPassword | string | The current password; required when changing your own password. |
| newPassword | string | The new password, 8 through 256 characters. |

### PromptParameter

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Placeholder name, written as {name} in a prompt. |
| description | string | What the placeholder is replaced with. |
| purposes | array | Debate steps where the placeholder may be used. |
| requiredFor | array | Debate steps whose prompts must include the placeholder. |

### PromptTemplate

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Prompt identifier. Factory prompts use stable pmt_factory_ identifiers. |
| name | string | Display name, 1 through 200 characters. |
| purpose | string | The debate step this prompt drives: question, response, synthesis, ratification or compaction. Fixed after creation. |
| description | string | Optional note about when to use this prompt, up to 1000 characters. |
| body | string | Prompt text with {parameter} placeholders. Use {{ and }} for literal braces. Up to 32000 characters. |
| factory | boolean | True for a factory default, which cannot be edited or deleted. |
| revision | integer | Optimistic revision. Updates and deletes must supply the current value. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

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

### RevealedCredential

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Credential identifier. |
| name | string | Display name. |
| kind | string | Credential kind. |
| parameterName | string | Header or query parameter name for header and query credentials; empty otherwise. |
| revision | integer | Revision of the revealed value. |
| secretValue | string | The decrypted value. A Codex login includes the refresh token the service manages. |

### RosterChangeRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| action | string | add, remove or replace. |
| participantId | string | Existing seat for remove/replace. |
| profileId | string | Reusable profile to snapshot for add/replace. |
| idempotencyKey | string | Unique command key. |
| revision | integer | Expected run revision. |

### RotateCredentialKeyRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| expectedKeyId | string | Active key ID from the last status read; stale requests fail without another rotation. |

### RunPrompt

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| purpose | string | Debate step. |
| promptId | string | Source prompt identifier at start time. |
| promptRevision | integer | Source prompt revision at start time. |
| name | string | Source prompt name at start time. |
| body | string | Frozen prompt text with placeholders. |

### RunnerProfile

CLI protocols are plain, claude, codex, gemini and mux. Named CLI providers fill omitted name, executable and empty arguments on create/update from assets/harness-defaults.json. Claude Code includes --dangerously-skip-permissions; Codex includes --yolo. Generic CLI (plain) requires an executable. CLI uses ordered arguments plus stdin. Mux defaults to print --output-format text --no-stats --no-session-persistence; custom arguments replace defaults. REST requires endpoint and model and supports native local tools (read_file, write_file, list_directory, run_process) when allowCli is enabled. workingDirectory applies to both runner kinds; empty uses the service directory. Tool activity is persisted as kind=tool messages, separate from answer text. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.

| Field | Type | Meaning |
| --- | --- | --- |
| modelProfileId | string | Reusable model reference. Empty retains inline legacy configuration. |
| roleInstructions | string | Role-specific participant instructions. |
| authBindings | array | Frozen HTTP bindings containing only nonsecret values and credential references. |
| credentialId | string | CLI credential reference. |
| contextMaxCharacters | integer | Exact model context character budget. |
| supportsTools | boolean | Declared tool support. |
| generation | ModelGenerationSettings | HTTP generation settings frozen with this participant snapshot. |
| modelRevision | integer | Model revision frozen at admission. |
| transportId | string | Endpoint or harness identity frozen at admission; endpoint capacity remains a live resource limit. |
| transportRevision | integer | Endpoint or harness revision frozen at admission. |
| restrictEnvironment | boolean | Restrict inherited process variables for reusable harness snapshots; legacy inline profiles retain their original behavior. |
| environmentVariables | array | Additional inherited variable names frozen at admission. Values are resolved only when the child starts. |
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
| maxOutputBytes | integer | Maximum captured output bytes; defaults to 64 MiB. |
| enabled | boolean | Whether new work can use this profile. |

### ServiceCapabilities

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| debateProtocol | integer | Current multi-agent debate protocol. |
| defaultPolicy | DebatePolicy | Default per-run policy; all accepted values are frozen on the run. |
| maximumPolicy | DebatePolicy | Maximum accepted per-run limits. Estimated tokens remain an accounting heuristic. |
| maxParticipants | integer | Maximum active seats accepted for a new roster. |
| deliveryModes | array | Supported per-interjection delivery modes. |
| consensusPolicy | string | Every active participant must explicitly agree. |
| rosterChanges | boolean | Roster changes are applied at safe boundaries. |

### SummarySection

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| kind | string | facts, contested_claims, positions, evidence, decisions, questions or constraints. |
| content | string | Source-grounded text, explicitly stating when this category has no established information. |

### SummarySource

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| messageId | string | Original conversation message identifier. |
| digest | string | Lowercase SHA-256 of the exact original UTF-8 content. |

### Tenant

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Tenant identifier. |
| name | string | Display name. |
| active | boolean | False disables sign-in and every token in the tenant. |
| protected | boolean | Protected tenants cannot be deleted or disabled. |
| revision | integer | Optimistic revision. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### TenantChoice

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Tenant ID to send as tenantId. |
| name | string | Tenant name. |

### TenantRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string | Display name, 1 through 200 characters. |
| active | boolean | False disables the tenant. |
| revision | integer | Expected revision when updating. |

### TenantRequiredError

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| code | string | Always tenant_required. |
| message | string | Human-readable explanation. |
| tenants | array | Tenants whose account accepted the password. |

### TokenDetails

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| principalType | string | User for a session, Credential for a bearer-token credential. |
| authScheme | string | Bearer or XToken. |
| sessionId | string | Session ID, or empty for a credential token. |
| credentialId | string | Credential ID, or empty for a session. |
| expiresUtc | string (nullable) | When the token stops working, if it expires. |
| user | User | Signed-in user. |
| tenant | Tenant | Tenant the token is bound to. |

### TokenResponse

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| token | string | Opaque session token for Authorization: Bearer or x-token. It is not stored by the service. |
| sessionId | string | Session ID, for listing and revoking sessions. |
| expiresUtc | string | When the token stops working unless refreshed. |
| user | User | Signed-in user. |
| tenant | Tenant | Tenant the session is bound to. |

### User

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | User identifier. |
| tenantId | string | Owning tenant. |
| firstName | string | First name. |
| lastName | string | Last name. |
| email | string | Email address, unique within the tenant. |
| isAdmin | boolean | System administrator: manages every tenant. |
| isTenantAdmin | boolean | Tenant administrator: manages users and credentials in this tenant. |
| active | boolean | False blocks sign-in and revokes access. |
| protected | boolean | Protected users cannot be deleted or disabled. |
| revision | integer | Optimistic revision. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### UserRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| firstName | string | First name. |
| lastName | string | Last name. |
| email | string | Email address, unique within the tenant. |
| password | string | Initial password, 8 through 256 characters, when creating. |
| isAdmin | boolean | System administrator flag; only system administrators may set it. |
| isTenantAdmin | boolean | Tenant administrator flag. |
| active | boolean | False disables the user. |
| revision | integer | Expected revision when updating. |

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
| maxParticipants | integer | Maximum active participants per debate, 2 through 64. Requires restart. |
| allowedHosts | array | Host names, besides 127.0.0.1 and localhost, that browsers and clients may use to reach the service, for example a reverse proxy name. ARGUEBOT_ALLOWED_HOSTS adds more at startup. |
| listenAddress | string | Listener address: 127.0.0.1 for native use, 0.0.0.0 inside a container with a loopback-published port. |
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
| maxParticipants | integer (nullable) | Maximum active seats per debate. |
| listenAddress | string (nullable) | Optional native or container listener address. Requires restart. |
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
