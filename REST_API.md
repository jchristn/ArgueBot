# REST API

The ArgueBot service owns persistent conversations, execution and configuration. This reference documents the implemented route contract; it is generated with the OpenAPI document and Postman collection from `assets/api-contract.json` and named C# DTOs. Implementation and release limitations are described in the [development documentation](docs/DEVELOPMENT.md).

## Authentication

Every route except `/health`, `GET /v2.0/api/login-info`, `POST /v2.0/api/token`, the OpenAPI document and the dashboard files needs a bearer token. Send it as `Authorization: Bearer <token>` or as `x-token: <token>`; if you send both, they must be the same token.

- **Session token**: `POST /v2.0/api/token` with `{ "email", "password", "tenantId" }`, or with the `x-email`, `x-password` and optional `x-tenant-id` headers. The response holds an opaque `abs_...` token that lasts 60 minutes. `POST /v2.0/api/token/refresh` replaces it with a new token and a fresh lifetime; the previous token stops working. `DELETE /v2.0/api/token` signs out.
- **Credential token**: a long-lived `abt_...` token created under `/v2.0/api/me/credentials`. Reveal it again with `POST .../credentials/{id}/reveal` (audited). Disable or delete the credential to revoke it.
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

## GET /v2.0/api/participants/{profileId}/checks

Read observed checks of an agent. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ModelCheck>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |
| pageNumber | query | false | Page to return, starting at 1. |

## POST /v2.0/api/participants/{profileId}/checks

Explicitly run a saved agent once, with its secret and model; potentially billable. Access: any signed-in user of an active tenant.

Success: **200**, response: `ModelCheck`. Request: `ModelCheckRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| profileId | path | true | Participant profile ID. Copy it from the Participants page. |

Representative request:

```json
{
  "idempotencyKey": "{{commandKey}}",
  "checkTools": false
}
```

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

## GET /v2.0/api/conversations/{conversationId}/runs/{runId}/questions

List the questions a run's agents asked, oldest first. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<QuestionRequest>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| runId | path | true | Run ID (run_...). Copy it from the run bar in the workspace. |

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

## GET /v2.0/api/resources

List resources in name order with optional search and tag filters. Access: any signed-in user of an active tenant.

Success: **200**, response: `ResourcePage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| search | query | false | Case-insensitive text to find in paths or titles. |
| tag | query | false | Backend filter. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## POST /v2.0/api/resources

Create a resource. Access: any signed-in user of an active tenant.

Success: **201**, response: `Resource`. Request: `ResourceRequest`.

Representative request:

```json
{
  "name": "billing",
  "title": "Billing",
  "description": "How invoices, refunds and credits work.",
  "tags": [
    "finance"
  ],
  "body": "# Billing\n\nInvoices are issued on the first business day of each month. Refunds over 500 USD need a second approver."
}
```

## POST /v2.0/api/resources/import

Import 1 to 50 markdown files; front matter supplies name, title, description, tags and locked. Access: any signed-in user of an active tenant.

Success: **201**, response: `ResourceImportResult`. Request: `ResourceImportRequest`.

Representative request:

```json
{
  "overwrite": false,
  "files": [
    {
      "fileName": "team-conventions.md",
      "content": "---\nname: team/conventions\ntitle: Team conventions\ndescription: Code review and naming rules.\ntags: [engineering]\n---\n\nUse descriptive branch names. Every change needs one review."
    }
  ]
}
```

## GET /v2.0/api/resources/by-name/{resourceName}

Read a resource by name; names may contain slashes. Access: any signed-in user of an active tenant.

Success: **200**, response: `Resource`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceName | path | true | Resource name, for example billing or team/conventions. Slashes in the name stay as path separators. |

## GET /v2.0/api/resources/{resourceId}

Read a resource. Access: any signed-in user of an active tenant.

Success: **200**, response: `Resource`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |

## PUT /v2.0/api/resources/{resourceId}

Update, rename, lock or unlock a resource with its expected revision; omitted fields are kept. Access: any signed-in user of an active tenant.

Success: **200**, response: `Resource`. Request: `ResourceRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |

Representative request:

```json
{
  "description": "How invoices, refunds and credits work, including approvals.",
  "body": "# Billing\n\nInvoices are issued on the first business day of each month. Refunds over 500 USD need a second approver. Credits expire after a year.",
  "revision": 1
}
```

## DELETE /v2.0/api/resources/{resourceId}

Delete a resource and its history with its expected revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |
| revision | query | false | Expected current revision; the request is refused if the resource changed. |

## GET /v2.0/api/resources/{resourceId}/revisions

List a resource's saved revisions newest first, without bodies. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ResourceRevision>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |

## GET /v2.0/api/resources/{resourceId}/revisions/{revision}

Read one saved revision with its body. Access: any signed-in user of an active tenant.

Success: **200**, response: `ResourceRevision`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |
| revision | path | true | Revision number, from the resource or its history. |

## POST /v2.0/api/resources/{resourceId}/revert

Restore a saved revision's title, description, tags and body as a new revision. Access: any signed-in user of an active tenant.

Success: **200**, response: `Resource`. Request: `ResourceRevertRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |

Representative request:

```json
{
  "targetRevision": 1,
  "revision": 2
}
```

## GET /v2.0/api/resources/{resourceId}/export

Export a resource as markdown with front matter. Access: any signed-in user of an active tenant.

Success: **200**, response: `string`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| resourceId | path | true | Resource ID (res_...). Copy it from the Resources page or GET /v2.0/api/resources. |

## GET /v2.0/api/questions

List questions agents asked, open first; scope mine or tenant. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| status | query | false | open, answered, skipped, expired, cancelled, or resolved for everything but open. Empty lists all. |
| conversationId | query | false | Only questions in this conversation (cnv_...). |
| runId | query | false | Only questions in this run (run_...). |
| scope | query | false | mine lists conversations you created; tenant lists every conversation in your tenant. |
| pageNumber | query | false | Page to return, starting at 1. |
| pageSize | query | false | Items per page, 1 through 100 unless noted. |

## GET /v2.0/api/questions/summary

Count open questions for badges and notifications. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionSummary`.

## GET /v2.0/api/questions/events

Read question changes after a cursor, or stream them with Accept: text/event-stream. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionEventPage`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| after | query | false | Cursor from the previous read (next) or the last event ID. Omit it to get the cursor of the newest change and no items, then read again to receive later changes. Streams also accept Last-Event-ID. |
| limit | query | false | Most changes per read. |
| Accept | header | false | Protocol control. |
| Last-Event-ID | header | false | SSE cursor formatted conversationId:sequence; foreign conversation cursors fail. |

## GET /v2.0/api/questions/{questionId}

Read a question request with its questions, options and answers. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| questionId | path | true | Question request ID (qrq_...). Copy it from the Questions page, the question dialog or questions list. |

## POST /v2.0/api/questions/{questionId}/answer

Answer every question of an open request; the first resolution wins. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionRequest`. Request: `QuestionAnswerRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| questionId | path | true | Question request ID (qrq_...). Copy it from the Questions page, the question dialog or questions list. |

Representative request:

```json
{
  "answers": [
    {
      "key": "environment",
      "selected": [
        "prod"
      ]
    },
    {
      "key": "window",
      "text": "Fifteen minutes"
    }
  ],
  "idempotencyKey": "{{commandKey}}"
}
```

## POST /v2.0/api/questions/{questionId}/skip

Skip an open request so the agent continues on stated assumptions. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionRequest`. Request: `QuestionSkipRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| questionId | path | true | Question request ID (qrq_...). Copy it from the Questions page, the question dialog or questions list. |

Representative request:

```json
{
  "note": "Assume production only.",
  "idempotencyKey": "{{commandKey}}"
}
```

## POST /v2.0/api/questions/{questionId}/defer

Answer later: end the waiting turn now; the step resumes after the answer. Access: any signed-in user of an active tenant.

Success: **200**, response: `QuestionRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| questionId | path | true | Question request ID (qrq_...). Copy it from the Questions page, the question dialog or questions list. |

## DELETE /v2.0/api/questions/{questionId}

Delete a resolved question whose debate has finished; the answer message stays in the conversation. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| questionId | path | true | Question request ID (qrq_...). Copy it from the Questions page, the question dialog or questions list. |

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

Create a bearer-token credential; reveal the token again later if needed. Access: any signed-in user of an active tenant.

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

Create a credential for a user; reveal the token again later if needed. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

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

## GET /v2.0/api/conversations/{conversationId}/shares

List a conversation's public links. Access: any signed-in user of an active tenant.

Success: **200**, response: `List<ConversationShare>`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

## POST /v2.0/api/conversations/{conversationId}/shares

Create a time-bound public link. Access: any signed-in user of an active tenant.

Success: **201**, response: `ConversationShareCreated`. Request: `ConversationShareRequest`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |

Representative request:

```json
{
  "expiresInHours": 168,
  "includeToolCalls": false
}
```

## DELETE /v2.0/api/conversations/{conversationId}/shares/{shareId}

Revoke a public link. Access: any signed-in user of an active tenant.

Success: **200**, response: `Acknowledgement`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| shareId | path | true | Public link ID (shr_...). Copy it from the Share dialog. |

## GET /v2.0/api/shares/{shareToken}

Read the transcript behind a public link without signing in. Access: public; no bearer token.

Success: **200**, response: `SharedTranscript`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| shareToken | path | true | The token at the end of a /share/ link. It is shown only when the link is created. |

## POST /v2.0/api/me/credentials/{credentialId}/reveal

Reveal one of your bearer tokens; audited. Access: any signed-in user of an active tenant.

Success: **200**, response: `RevealedToken`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

## POST /v2.0/api/users/{userId}/credentials/{credentialId}/reveal

Reveal a user's bearer token; audited. Access: tenant administrators (and system administrators) of the caller's tenant. A system administrator may send x-tenant-id to act in another tenant; the bypass is audited.

Success: **200**, response: `RevealedToken`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| userId | path | true | User ID (usr_...). Copy it from Administration > Users. |
| credentialId | path | true | Bearer-token credential ID (crd_...). Copy it from the credential list. |

## POST /v2.0/api/conversations/{conversationId}/shares/{shareId}/reveal

Show an active public link again; audited. Access: any signed-in user of an active tenant.

Success: **200**, response: `ConversationShareCreated`.

| Parameter | Location | Required | Meaning |
| --- | --- | --- | --- |
| conversationId | path | true | Conversation ID (cnv_...). Copy it from the workspace heading or a conversation list. |
| shareId | path | true | Public link ID (shr_...). Copy it from the Share dialog. |

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

### AttemptResource

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| resourceId | string | Resource ID (res_...). |
| name | string | Resource name at the time of the call. |
| revision | integer | Revision read, or the revision a write produced. |
| access | string | read or write. |
| tool | string | Tool used: read_resource, create_resource, update_resource, edit_resource or append_resource. |
| bytes | integer | UTF-8 bytes written; zero for reads. |
| createdUtc | string | Time of the call in UTC. |

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

### ConversationShare

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Share ID. |
| conversationId | string | Shared conversation. |
| createdByUserId | string | User who created the link. |
| tokenLast4 | string | Last four characters of the link token, to tell links apart. |
| includeToolCalls | boolean | Whether readers see tool calls and results. |
| expiresUtc | string | When the link stops working. |
| revokedUtc | string (nullable) | When the link was revoked. |
| lastViewedUtc | string (nullable) | Last time someone opened the link. |
| viewCount | integer | How many times the link was opened. |
| active | boolean | False once revoked or expired. |
| createdUtc | string | When the link was created. |

### ConversationShareCreated

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| share | ConversationShare | The stored link. |
| token | string | Link token. Anyone with it can read the transcript until it expires or is revoked. |
| path | string | Dashboard path for the link, /share/{token}. |
| url | string | Absolute link built from the host this request used. |

### ConversationShareRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| expiresInHours | integer | Hours until the link stops working, 1 through 2160 (90 days). |
| includeToolCalls | boolean | Show tool calls and results to readers. |

### CreateRunRequest

Seat IDs (participantIds and orchestratorId) are agent IDs (par_...); a CLI agent brings its optional stored secret and model. A reusable HTTP model ID (mod_...) is accepted as a shortcut and runs on its endpoint with no role instructions. Protocol 2 on /v2.0 requires an explicit orchestratorId and 2 through the advertised maximum participant IDs; consensus requires every active seat to agree on one proposal. Protocol 1 retains two or three distinct participant IDs and accepts a question up to 4000 characters; blank uses the conversation title. Followup requires one participant and a nonempty question, and forces one round with no countdown.

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
| resourceAccess | string | What the debate's agents may do with the tenant's resources: off (no resource tools), read (the default: list, search and read) or read_write (also create, update, edit and append). |
| resourceNames | array | Optional names of the only resources this debate's agents can see, up to 200. With resourceTags, a resource is visible when it matches either list. Both empty means every resource. |
| resourceTags | array | Optional tags, up to 16; resources with any of them are visible. Resources the agents create are visible to them for the rest of the debate. |
| maxResourceWritesPerTurn | integer | Most resource writes (create, update, edit and append) in one agent turn, 0 through 100. Default 8. |
| maxResourceWriteBytesPerTurn | integer | Most UTF-8 bytes of resource text written in one agent turn, 0 through 1048576. Default 65536. |
| maxResourcesCreated | integer | Most resources the debate's agents may create in total, 0 through 1000. Default 50. |
| maxStepRetries | integer | Automatic retries for one step before the debate pauses, 0 through 10. Default 3. Covers correction requests after an invalid question, proposal, vote or summary, and repeats after a transient failure (rate limit, provider outage, dropped connection, agent exit or turn timeout), with a growing wait between repeats. 0 pauses at the first failure. A repeated turn can repeat external work. |
| userQuestions | string | Which agents may ask the user questions with ask_user: off, orchestrator (the orchestrator and a follow-up answerer only) or all (every seat). Default all. |
| intake | string | Whether the debate opens with an intake step in which the orchestrator may ask the user about the question before round 1: off or ask_if_needed. Default ask_if_needed for new debates; debates created before 2.3.0 have off. |
| maxIntakeQuestions | integer | Most questions the intake request may carry, 1 through 4. Default 4. |
| questionHoldSeconds | integer | Seconds a waiting ask_user call stays open for the answer before the turn is parked and resumed later, 0 through 1800. Default 300. 0 parks at once. |
| questionExpirySeconds | integer | Seconds after which an unanswered question is treated as skipped, 0 through 2592000. Default 0, which never expires. |
| resumeOnAnswer | boolean | Whether the debate continues by itself once every waiting question is resolved. When false it pauses for Resume. Default true. |
| maxQuestionRequestsPerTurn | integer | Most ask_user requests in one agent turn, 0 through 4. Default 2. |
| maxQuestionRequestsPerRun | integer | Most ask_user requests in the whole debate, 0 through 200. Default 20. |
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
| waitingSeconds | integer | Seconds the run has spent waiting for the user's answers. Excluded from the wall-clock budget. |
| awaitingSinceUtc | string (nullable) | When the run started waiting for an answer; null while it is not waiting. |
| resumeQuestionId | string | Question whose answer the next attempt resumes from, when the previous attempt of this step ended waiting for the user. Derived, not stored. |
| resumeAttemptId | string | The attempt that ended waiting for the user and that the next attempt resumes. Derived, not stored. |
| repairPending | boolean | True when the previous step returned invalid structured output and the next attempt is a correction request within the step retry limit. |

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
| resourcesCreated | integer | Resources this run's agents created. |
| questionRequests | integer | ask_user requests the debate's agents have made. |
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
| repair | boolean | True when this attempt repeated a question, proposal, vote or summary after invalid structured output. The debate's step retry limit bounds the repairs. |
| repairNumber | integer | Correction request number for this step: 0 for the first try, then 1, 2 and so on up to the debate's step retry limit. |
| contextEpoch | integer | Moderator context frozen before invocation. |
| rosterRevision | integer | Roster revision frozen before invocation. |
| endpointWaitMilliseconds | integer | Elapsed milliseconds waiting for shared endpoint capacity; zero for immediate admission, CLI or older records. |
| credentialRevisions | array | Credential identities and revisions actually resolved for this invocation; never credential values. |
| resources | array | Resources this attempt read or wrote through the resource tools, in call order. |

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
| round | integer (nullable) | Debate round in which this message was generated, from its generation attempt. Null for moderator messages, tool records and single-message reads. |

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
| transportId | string | Frozen endpoint identity; empty for an agent check. |
| transportRevision | integer | Frozen endpoint revision; zero for an agent check. |
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
| endpointId | string | HTTP endpoint the model runs on. |
| model | string | Provider model identifier. |
| contextMaxCharacters | integer | Conservative UTF-16 character budget for this model; not a token count. |
| maxOutputBytes | integer | Service output capture ceiling, 1024 through 67108864 bytes, for HTTP models. |
| generation | ModelGenerationSettings | Optional HTTP sampling and generated-token overrides. |
| supportsTools | boolean | Whether the model declares tool support. |
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
| warnings | array | Problems that do not block saving, found when the prompt is read or saved: for example a read_resource reference to a resource that does not exist in this tenant. Not stored. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### Question

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable question identifier (qst_...). |
| key | string | Identifier chosen by the agent, 1 through 32 characters, unique within the request, so it can match answers to questions. |
| header | string | Short label, up to 40 characters, used for chips and lists. |
| prompt | string | The question, up to 1000 characters. Agent-written text, shown as data. |
| kind | string | text, single_choice, multi_choice or confirm. |
| options | array | Choices for single_choice and multi_choice, 2 through 12; empty for other kinds. |
| allowOther | boolean | Whether a choice question also accepts free text as Other. |
| minSelections | integer | Fewest options a multi_choice answer selects, 0 through MaxSelections. Default 1 for required questions. |
| maxSelections | integer | Most options a multi_choice answer selects, up to the option count. Default the option count. |
| maxLength | integer | Longest text answer in characters, 1 through 8000. Default 8000. |
| placeholder | string | Optional hint shown in an empty text answer, up to 120 characters. |
| required | boolean | Whether the question must be answered unless the whole request is skipped. |
| answer | QuestionAnswer (nullable) | The answer, once the request is answered or skipped. |

### QuestionAnswer

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| skipped | boolean | Whether the user left this optional question unanswered, or skipped the whole request. |
| selected | array | Selected option keys for single-choice and multi-select questions, in the order shown. |
| selectedLabels | array | Labels of the selected options, in the same order as Selected. |
| otherText | string | Free text entered for Other, when the question allows it. |
| text | string | Answer text for a text question. |
| confirmed | boolean (nullable) | Yes (true) or no (false) for a confirm question; null otherwise. |
| answeredByUserId | string | User who answered. |

### QuestionAnswerInput

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| questionId | string | Question identifier (qst_...). Either QuestionId or Key is required. |
| key | string | Question key chosen by the agent. Either QuestionId or Key is required. |
| skip | boolean | Leave this optional question unanswered. |
| selected | array | Selected option keys for single_choice (exactly one, unless Other is used) and multi_choice. |
| otherText | string | Free text for Other, when the question allows it, up to 2000 characters. |
| text | string | Answer for a text question, up to the question's MaxLength. |
| confirmed | boolean (nullable) | Yes or no for a confirm question. |

### QuestionAnswerRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| answers | array | One entry per question. Required questions need an answer; optional ones may be skipped or omitted. |
| idempotencyKey | string | Client key, up to 128 characters, so a repeated submit returns the same result instead of a conflict. |

### QuestionEvent

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| cursor | string | Opaque cursor; pass it as after to read later changes. |
| requestId | string | Question request that changed. |
| conversationId | string | Conversation of the request. |
| status | string | opened, held, parked, answered, skipped, expired or cancelled. |
| createdUtc | string | When the change happened. |

### QuestionEventPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Changes, oldest first. |
| next | string | Cursor to pass as after for the next read. Equal to the request's cursor when nothing changed. |

### QuestionOption

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable option identifier (qop_...). |
| key | string | Option identifier chosen by the agent, 1 through 32 characters, unique within its question. Answers report the selected keys. |
| label | string | Option text shown to the user, up to 80 characters. |
| description | string | Optional explanation shown under the label, up to 300 characters. |
| recommended | boolean | Whether the agent recommends this option. At most one option of a single-choice question is recommended. |

### QuestionPage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Requests on this page. |
| pageNumber | integer | One-based page number. |
| pageSize | integer | Requests per page. |
| totalCount | integer | Requests matching the filter. |

### QuestionRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Request identifier (qrq_...). |
| conversationId | string | Conversation the request belongs to. |
| conversationTitle | string | Conversation title, for inbox lists. |
| runId | string | Run whose agent asked. |
| attemptId | string | Generation attempt that asked. |
| askerId | string | Seat or orchestrator profile that asked. |
| askerName | string | Display name of the asking seat. |
| askerRole | string | participant, orchestrator or followup. |
| phase | string | Debate phase when the agent asked, such as intake, question or response. |
| round | integer | Debate round when the agent asked. |
| context | string | Why the agent is asking, up to 2000 characters of markdown. Agent-written text, shown as data. |
| wait | boolean | Whether the turn waits for the answer. A request that does not wait delivers its answer to the asking seat's next turn. |
| visibility | string | panel (every seat sees the answer) or self (only the asking seat). |
| status | string | open, answered, skipped, expired or cancelled. |
| holdState | string | held (a live tool call is waiting), parked (the turn ended; a resume attempt follows the answer) or none. |
| resolutionReason | string | For a skipped request: skipped_by_user or expired. For a cancelled request: run_stopped. |
| resolutionNote | string | Optional note the user added when skipping. |
| resolvedByUserId | string | User who answered or skipped. |
| answerMessageId | string | Transcript message that carries the answer to the panel. |
| resumeAttemptId | string | Attempt that resumed the parked turn with the answer. |
| credentialWarning | boolean | Whether the question text looks like a request for a password, token or key. Interfaces show a stronger warning. |
| expiresUtc | string (nullable) | When an unanswered request expires; null when it never expires. |
| resolvedUtc | string (nullable) | When the request was resolved. |
| createdUtc | string | When the agent asked. |
| runState | string | State of the debate run that asked, such as awaiting_input or completed. |
| deletable | boolean | Whether the request can be deleted: it is resolved and its debate has finished, so no turn or later prompt still depends on it. |
| questions | array | One through four questions, in order. |

### QuestionSkipRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| note | string | Optional note passed to the agent, up to 2000 characters. |
| idempotencyKey | string | Client key, up to 128 characters, so a repeated skip returns the same result. |

### QuestionSummary

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| openMine | integer | Open requests in conversations the caller created. |
| openTenant | integer | Open requests in the whole tenant. |
| waitingRuns | integer | Runs in the tenant waiting for an answer. |
| newestId | string | Newest open request in the tenant, or empty. |

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

### Resource

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable resource identifier (res_...). Names can change; the ID does not. |
| name | string | Unique name within the tenant, 1 through 128 characters of lowercase letters, digits, hyphen, underscore, period and slash, for example billing or team/conventions. Agents pass it to read_resource. |
| title | string | Display title, up to 200 characters. Empty uses the name. |
| description | string | One-line summary, up to 500 characters, shown by list_resources so an agent can decide what to read. |
| tags | array | Up to 16 lowercase tags, each 1 through 64 characters of letters, digits, hyphen, underscore, period and slash. |
| body | string | Markdown content, up to 262144 UTF-8 bytes (256 KiB). NUL characters are not allowed. |
| characters | integer | Body length in UTF-16 characters, the unit read_resource offsets use. |
| bytes | integer | Body size in UTF-8 bytes. |
| locked | boolean | When true, agents can read but never change this resource. Users can still edit it. |
| revision | integer | Optimistic revision, starting at 1 and increasing with every change. Updates, deletes and reverts must supply the current value. |
| createdByUserId | string | User who created the resource, or the user whose debate created it. |
| updatedByKind | string | Who made the latest change: user or agent. Agent-written text persists into later debates, so treat it as unreviewed until a user checks it. |
| updatedById | string | User ID (usr_...) or agent seat ID (par_...) that made the latest change. |
| updatedByName | string | Display name of the user or agent that made the latest change. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

### ResourceImportFile

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| fileName | string | Original file name. Without a name in the front matter, the name comes from the file name without its .md extension. |
| content | string | File text, front matter included. |

### ResourceImportRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| files | array | Files to import. |
| overwrite | boolean | When true, a file whose name already exists replaces that resource's content as a new revision. When false (the default), an existing name fails the import with resource_exists. |

### ResourceImportResult

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Imported resources in file order. |
| created | integer | Number of new resources. |
| updated | integer | Number of existing resources replaced. |

### ResourcePage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| items | array | Resources on this page. |
| pageNumber | integer | One-based page number. |
| pageSize | integer | Resources per page, 1 through 200. |
| totalCount | integer | Resources matching the filter. |

### ResourceRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| name | string (nullable) | Unique name within the tenant: 1 through 128 characters of letters, digits, hyphen, underscore, period and slash. It is stored in lowercase. Required on create; on update, a new value renames the resource. |
| title | string (nullable) | Display title, up to 200 characters. |
| description | string (nullable) | One-line summary, up to 500 characters. |
| tags | array (nullable) | Up to 16 tags, each 1 through 64 characters. Stored in lowercase without duplicates. |
| body | string (nullable) | Markdown content, up to 262144 UTF-8 bytes. The limit counts bytes, not characters. |
| locked | boolean (nullable) | Whether agents are prevented from changing the resource. |
| revision | integer | Expected current revision. Required on update; ignored on create. |

### ResourceRevertRequest

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| targetRevision | integer | Revision to restore. It must still be in the saved history. |
| revision | integer | Expected current revision of the resource. |

### ResourceRevision

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Revision record identifier (rsr_...). |
| resourceId | string | Resource this revision belongs to (res_...). |
| revision | integer | Revision number the change produced. |
| name | string | Resource name at this revision. |
| title | string | Title at this revision. |
| description | string | Description at this revision. |
| tags | array | Tags at this revision. |
| body | string | Full markdown body at this revision. Empty in revision lists; read one revision to get it. |
| characters | integer | Body length in UTF-16 characters. |
| locked | boolean | Whether the resource was locked at this revision. |
| change | string | What changed: create, update, rename, lock, unlock, append, revert or import. |
| authorKind | string | user or agent. |
| authorId | string | User ID (usr_...) or agent seat ID (par_...) that made the change. |
| authorName | string | Display name of the author. |
| runId | string | Debate run whose agent made the change (run_...), empty for user changes. |
| attemptId | string | Generation attempt that made the change, empty for user changes. |
| createdUtc | string | Time of the change in UTC. |

### ResourceSummary

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Stable resource identifier (res_...). Names can change; the ID does not. |
| name | string | Unique name within the tenant, 1 through 128 characters of lowercase letters, digits, hyphen, underscore, period and slash, for example billing or team/conventions. Agents pass it to read_resource. |
| title | string | Display title, up to 200 characters. Empty uses the name. |
| description | string | One-line summary, up to 500 characters, shown by list_resources so an agent can decide what to read. |
| tags | array | Up to 16 lowercase tags, each 1 through 64 characters of letters, digits, hyphen, underscore, period and slash. |
| characters | integer | Body length in UTF-16 characters, the unit read_resource offsets use. |
| bytes | integer | Body size in UTF-8 bytes. |
| locked | boolean | When true, agents can read but never change this resource. Users can still edit it. |
| revision | integer | Optimistic revision, starting at 1 and increasing with every change. Updates, deletes and reverts must supply the current value. |
| createdByUserId | string | User who created the resource, or the user whose debate created it. |
| updatedByKind | string | Who made the latest change: user or agent. Agent-written text persists into later debates, so treat it as unreviewed until a user checks it. |
| updatedById | string | User ID (usr_...) or agent seat ID (par_...) that made the latest change. |
| updatedByName | string | Display name of the user or agent that made the latest change. |
| createdUtc | string | Creation time in UTC. |
| updatedUtc | string | Last change time in UTC. |

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

### RevealedToken

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Credential ID. |
| name | string | Credential name. |
| token | string | The bearer token. |

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

An agent. CLI protocols are plain, claude, codex, gemini and mux. A CLI agent may name a stored secret in credentialId (a Claude Code token for claude, a Codex login for codex); select one when running in Docker or to override the credentials the program uses by default. model is optional for CLI agents and is passed as --model to Claude Code, Codex and Gemini, or into a literal {model} argument. Named CLI providers fill omitted name, executable and empty arguments on create/update from assets/harness-defaults.json. Claude Code includes --dangerously-skip-permissions; Codex and Mux include --yolo. Generic CLI (plain) requires an executable. CLI uses ordered arguments plus stdin. Mux defaults to print --yolo --output-format text --no-stats --no-session-persistence; custom arguments replace defaults. REST requires endpoint and model and supports native local tools (read_file, write_file, list_directory, run_process) when allowCli is enabled. workingDirectory applies to both runner kinds; empty uses the service directory. Tool activity is persisted as kind=tool messages, separate from answer text. secretEnvironmentVariable is a reference, never the secret itself. Profiles are frozen into relational snapshots for each run.

| Field | Type | Meaning |
| --- | --- | --- |
| modelProfileId | string | Reusable model reference. Empty retains inline legacy configuration. |
| roleInstructions | string | Role-specific participant instructions. |
| authBindings | array | Frozen HTTP bindings containing only nonsecret values and credential references. |
| credentialId | string | Optional stored secret that signs in a CLI agent: a Claude Code token for claude or a Codex login for codex. Empty uses the service machine's own login. |
| contextMaxCharacters | integer | Exact model context character budget. |
| supportsTools | boolean | Declared tool support. |
| generation | ModelGenerationSettings | HTTP generation settings frozen with this participant snapshot. |
| modelRevision | integer | Model revision frozen at admission. |
| transportId | string | Endpoint identity frozen at admission; endpoint capacity remains a live resource limit. |
| transportRevision | integer | Endpoint revision frozen at admission. |
| restrictEnvironment | boolean | Restrict a CLI agent to standard process variables plus the named environmentVariables; false passes the service environment. |
| environmentVariables | array | Additional inherited variable names frozen at admission. Values are resolved only when the child starts. |
| id | string | Stable profile identifier. |
| name | string | Participant display name. |
| kind | string | Runner kind: cli or rest. |
| provider | string | Protocol adapter. |
| endpoint | string | REST base URI. |
| model | string | Model identifier: required for HTTP agents; optional for CLI agents, passed as --model to Claude Code, Codex and Gemini or into a literal {model} argument. |
| executable | string | Direct executable path or name. |
| arguments | array | Ordered arguments; prompts use standard input. |
| workingDirectory | string | Directory on the execution host for CLI agents and REST local tools; empty uses the service directory. |
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

### SharedMessage

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Message ID. |
| runId | string | Debate the turn belongs to, or empty for a moderator note. |
| speaker | string | Speaker name, or user for the moderator. |
| kind | string | Turn kind, for example question, answer, proposal, ratification or tool. |
| state | string | completed, failed or interrupted. |
| content | string | Complete text. |
| createdUtc | string | When the turn was written. |

### SharedProposal

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Proposal ID. |
| runId | string | Debate. |
| content | string | Proposed answer. |
| superseded | boolean | True when a later proposal replaced it. |
| agreements | array | Votes. |

### SharedRun

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Run ID. |
| state | string | Run state. |
| outcome | string | Outcome, when finished. |
| round | integer | Current or final round. |
| maxRounds | integer | Round limit. |
| question | string | The question the debate answered. |
| protocolVersion | integer | 1 for a fixed panel, 2 for an orchestrated debate. |
| createdUtc | string | When the debate started. |
| orchestrator | SharedSeat (nullable) | Orchestrator, for protocol 2. |
| participants | array | Debating agents. |

### SharedSeat

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| id | string | Seat ID. |
| name | string | Display name. |

### SharedTranscript

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| title | string | Conversation title. |
| openingQuestion | string | Opening question. |
| expiresUtc | string | When the link stops working. |
| includeToolCalls | boolean | Whether tool turns are included. |
| runs | array | Debates, oldest first. |
| messages | array | Turns in order. Private directives are never included. |
| proposals | array | Proposals for protocol 2 debates. |

### SharedVote

Named fields shared by the service and its clients. Nullable fields are optional; endpoint-specific requirements above still apply.

| Field | Type | Meaning |
| --- | --- | --- |
| participantId | string | Seat ID of the voter. |
| messageId | string | Turn that holds the vote. |
| position | string | agree, disagree, abstain or needs_information. |
| rationale | string | The agent's reason. |
| superseded | boolean | True when a later vote replaced it. |

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
| maxLiveQuestionHolds | integer | Most ask_user calls held open for an answer at once across the service, 0 through 64. Default 4. Above it, waiting questions park at once and their turns resume after the answer. Requires restart. |
| maxParticipants | integer | Maximum active participants per debate, 2 through 64. Requires restart. |
| allowedHosts | array | Host names, besides 127.0.0.1 and localhost, that browsers and clients may use to reach the service, for example a reverse proxy name. ARGUEBOT_ALLOWED_HOSTS adds more at startup. * allows any host name. |
| listenAddress | string | Listener address: 127.0.0.1 for native use, 0.0.0.0 inside a container with a loopback-published port. |
| allowCli | boolean | Whether CLI agents and REST local file/process tools may execute. Default true for local mode only. |

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
| maxLiveQuestionHolds | integer (nullable) | Optional limit on ask_user calls held open at once, 0 through 64. Requires restart. Omission preserves the configured value. |
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
