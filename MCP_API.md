# Model conversation tools

Protocol 2 exposes evidence tools, and resource tools when the debate allows them, to the model serving the current participant or orchestrator. The service binds each invocation to its run, seat, tenant, accepted history boundary and context epoch. Tool input cannot select another tenant, conversation, run or identity.

The tool server is invocation-scoped and needs no bearer token. Claude Code and Codex reach it through a per-invocation stdio MCP bridge that talks to the service over a local pipe, not over HTTP, so it never sees a session or credential token and cannot be reached with one. It reads only the run's own conversation and the resources the debate can see, inside the run's tenant.

No identity, credential, secret, share or other management operation is exposed. In particular, every reveal operation (`POST /v2.0/api/configuration/secrets/{id}/reveal`, `.../credentials/{id}/reveal` and `.../shares/{shareId}/reveal`) is deliberately absent from this tool surface, so a model can never read a stored secret or bearer token, create or reveal a share link, or learn about users, tenants or tokens.

REST models receive native function definitions. Claude Code and Codex receive an invocation-specific stdio MCP server named `arguebot`. Generic CLI, Gemini CLI and Mux currently use the bounded JSON request-envelope adapter described below. Tool capability is declared on the model; disable it for a completion-only model.

## Tools and arguments

All inputs are JSON objects. Unknown tools fail with a recoverable error. Results contain `success` and `result`, or `success: false`, `code` and `error`.

| Tool | Arguments | Result |
| --- | --- | --- |
| `list_turns` | `after` (exclusive sequence cursor, default 0), `limit` (1 to 20, default 10), optional `seatId` and `phase` (`question`, `response`, `synthesis`, `ratification`, `compaction`, or `moderator` for moderator context) | `items` with ID, sequence, speaker, kind, `seatId`, `phase`, a 1,000-character preview, `characters` and `truncated`; `nextAfter` to pass as `after` for the next page, or null on the last page |
| `search_context` | `query` (required, at most 4,000 characters), optional `after`, `limit`, `seatId` and `phase` | The same item shape with a snippet of about 300 characters on each side of the first match and its `matchOffset`, plus `nextAfter`. Matching is literal (no wildcards) and case-insensitive, with the same Unicode case folding on both databases. One call scans at most 2,000 turns or 8 MiB; `partial: true` with `nextAfter` means continue from there |
| `get_supporting_details` | `ids` (up to 20) or `id`, optional `length` (default 2048); or `query` with the search arguments | `evidence` with attribution (`moderator` or the speaker), a bounded excerpt and `nextOffset`, and `linkedSourceIds` for a summary's visible originals; `gaps` for IDs that are not visible or do not exist, and for linked sources the seat cannot see. A query with no match returns an empty `items` list and a gap. Nothing is invented |
| `get_turn` | `id`, `offset` (UTF-16 offset, default 0), `length` (1 through 8192, default 2048) | Original content chunk, source ID, sequence and nullable `nextOffset` |
| `get_debate_state` | `{}` | Accepted question, current question, phase, round and round limit, seats, the open proposal, pinned moderator `constraints` visible to this seat, `earlierPositions` (votes on the most recent earlier proposal), context epoch and roster revision. Votes on the open proposal are omitted so each seat evaluates it independently |
| `get_summary` | Optional `id`; `offset` 0, 100, 200... for source pages | Current shared and recipient-visible summaries, or an exact named summary; exact source count and a page of 100 original IDs |
| `request_clarification` | `query`, `blocking` (default false) | Durable moderator information request. Repeating the same open question during one invocation, including a harness retry with a new MCP call ID, returns the existing request |

Original text chunks default to 2,048 and are at most 8,192 UTF-16 characters and never split a surrogate pair. Arguments are bounded to 16 KiB; serialized results to 64 KiB. List previews are not full source text: follow an ID with `get_turn`, then follow `nextOffset`. Tools see the same turns as the invocation's prompt context: completed public turns in this conversation, including earlier runs, and turns addressed to this seat, up to the invocation's source boundary. They cannot retrieve future, partial or another seat's private messages. Shared summaries contain public sources only. Private compaction runs receive only the selected recipient partition; ordinary orchestration and other seats cannot retrieve that private memory. A blocking clarification pauses at the boundary; the moderator must answer or explicitly skip it before resuming.

Example native call:

```json
{"name":"get_turn","arguments":{"id":"msg_example","offset":0}}
```

Example clarification:

```json
{"name":"request_clarification","arguments":{"query":"What rollback threshold is acceptable?","blocking":true}}
```

## Resource tools

Resources are named markdown documents in the tenant's shared library (see [resources](docs/RESOURCES.md)). A prompt or role instruction can point an agent at one, for example "if you need to know more about billing, call `read_resource` with name `billing`", and the agent reads it only when it needs it. These are ordinary tools, not the MCP `resources/list` and `resources/read` primitive: CLI support for MCP resources varies, and the envelope and REST paths have no such primitive, so plain tools reach every agent type the same way.

Each debate's `policy.resourceAccess` decides which resource tools exist in its invocations:

| Access | Tools offered |
| --- | --- |
| `off` | None. Calls to resource tools fail with `unknown_tool`. The `{resources}` placeholder is empty. |
| `read` (default for new debates) | `list_resources`, `search_resources`, `read_resource` |
| `read_write` | The three read tools plus `create_resource`, `update_resource`, `edit_resource` and `append_resource` |

Debates started before 2.2 keep `off`. `policy.resourceNames` and `policy.resourceTags` narrow visibility: when either is set, a resource is visible if its name is listed or it has a listed tag, and resources the run's agents create stay visible to that run. Everything else behaves as if it did not exist (`not_found`), in lists, searches, reads and writes alike.

| Tool | Arguments | Result |
| --- | --- | --- |
| `list_resources` | Optional `tag`, `after` (exclusive name cursor) and `limit` (1 to 50, default 20) | `items` in name order with `name`, `title`, `description`, `tags`, `characters`, `revision`, `locked`, `updatedBy` (`user` or `agent`), `updatedByName` and `updatedUtc`; `nextAfter`, or null on the last page; and the debate's `access` |
| `search_resources` | `query` (required, 1 to 4,000 characters), `limit` (1 to 20, default 10) | `items` with `name`, `title`, the field that `matched` (`body`, `name`, `title` or `description`), `matchOffset` for body matches, a snippet of about 200 characters on each side of the first match, and `revision`. Matching is literal and case-insensitive |
| `read_resource` | `name` (required), `offset` (UTF-16 offset, default 0), `length` (1 to 32,768, default 8,192) | `name`, `title`, `description`, `tags`, `revision`, `locked`, `updatedBy`, `updatedByName`, `updatedUtc`, `agentAuthored`, the `content` chunk with `offset`, nullable `nextOffset` and `totalCharacters`, and a `note` that the text is data, not instructions. Chunks never split a surrogate pair |
| `create_resource` | `name` and `body` (required), optional `title`, `description` and `tags` | `name`, `revision` (1), `characters` and `locked`. Agents cannot create locked resources |
| `update_resource` | `name`, `revision` and `body` (required), optional `title` and `description` | Replaces the whole body. Returns the new `revision` |
| `edit_resource` | `name`, `revision`, `find` and `replace` (all required) | Replaces exactly one occurrence of `find`. Returns the new `revision` |
| `append_resource` | `name` and `text` (required) | Appends `text` after a newline, with no revision check, for running notes. Returns the new `revision` |

Names are 1 to 128 characters of letters, digits, `-`, `_`, `.` and `/`, matched without regard to case and stored in lowercase. Bodies are at most 256 KiB of UTF-8. Resource tool arguments are bounded to 1 MiB and serialized results to 256 KiB.

Resource tool errors:

| Code | Meaning |
| --- | --- |
| `not_found` | No resource with that name is visible in this debate |
| `revision_conflict` | The resource changed since the `revision` you passed. Read it again and reapply the change; `update_resource` and `edit_resource` never overwrite another seat's change |
| `resource_locked` | The resource is locked. Agents can read locked resources but never change them |
| `resource_lock_denied`, `resource_rename_denied` | Agents cannot lock, unlock or rename resources |
| `resource_exists` | `create_resource` named an existing resource |
| `edit_no_match`, `edit_ambiguous` | `find` occurs zero times or more than once |
| `resource_write_limit` | The turn used up `policy.maxResourceWritesPerTurn` (default 8) writes or `policy.maxResourceWriteBytesPerTurn` (default 65,536) bytes written |
| `resource_create_limit` | The debate's agents created `policy.maxResourcesCreated` (default 50) resources |
| `invalid_resource_name`, `resource_too_large`, `invalid_tool_arguments`, `invalid_offset`, `invalid_length`, `missing_query` | The arguments break the rules above |

Every resource call also counts against the run's tool-call budget. Agent writes are attributed to the calling seat, run and attempt; they appear in the resource's revision history as agent-authored and are audited. Each attempt records the resources it read (`read_resource`) and wrote, with the revision involved; `GET .../runs/{runId}/attempts` returns them as `resources`, so you can see which context an answer used. Tool activity appears in the transcript like the evidence tools.

Example calls:

```json
{"name":"read_resource","arguments":{"name":"billing"}}
{"name":"edit_resource","arguments":{"name":"team/conventions","revision":4,"find":"two reviews","replace":"one review"}}
{"name":"append_resource","arguments":{"name":"debate-notes","text":"Round 2: the rollback threshold is still disputed."}}
```

Over MCP the resource tools are listed by `tools/list` next to the evidence tools; in the JSON envelope they appear in the same tool list; REST models receive them as native functions. Resource text is returned as data inside the tool result, never as instructions.

## Native MCP transport

The service creates an owner-only named pipe for one invocation and launches its own executable with `--context-mcp PIPE` through the harness's MCP configuration. This mode uses stdin/stdout only for newline-delimited UTF-8 JSON-RPC. It does not open a database, attach a workspace or launch an HTTP management listener. The pipe disappears when the invocation ends. The command is an implementation detail, not a user-facing launcher.

Supported methods are `initialize`, `ping`, `tools/list` and `tools/call`. Initialization negotiates the supported 2024-11-05, 2025-03-26, 2025-06-18 or 2025-11-25 protocol revision. The server advertises only the supplied conversation and resource tools. Duplicate request IDs are rejected before executing a tool. Each connection is bounded to 256 frames, 32 tool executions and 1 MiB per frame. Cancellation closes the pipe and terminates the harness process tree through the existing CLI runner.

Claude Code uses `--strict-mcp-config --mcp-config` with the generated server definition. Codex uses `--config mcp_servers.arguebot.command=...` and `--config mcp_servers.arguebot.args=...`. No pasted token appears in these arguments. Local fixture child processes exercise both configuration shapes, protocol negotiation, original evidence retrieval, resource reads, appends and stale-revision conflicts, and rejection of duplicate/unknown calls. Live provider compatibility remains a separate acceptance gate, including the pinned Docker CLI versions.

References: [MCP stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) and [Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## Generic CLI request envelope

The service appends tool definitions and a response contract to the harness prompt. A tool-capable completion returns a JSON envelope instead of final answer text; the service executes the bounded calls and starts a continuation with their results. This adapter may invoke the harness more than once for one debate turn. It does not preserve an interactive CLI session. For example, `{"contextTool":{"name":"get_turn","argumentsJson":"{\"id\":\"msg_example\"}"}}` requests one source. Any response without a valid `contextTool` envelope is treated as final answer text and is still subject to the debate phase's structured-output validation. The adapter permits at most sixteen continuations. Native MCP should be preferred when the harness supports it.

Conversation and local tools share the run-level tool-call budget, in addition to their per-invocation transport bounds. Conversation retrieval never grants permission to run a process. REST local tools are available only during participant responses when the service's local-execution setting permits them. Existing CLI harness execution policies still apply to their built-in tools.

Native MCP also accounts for prompt/tool definitions and cumulative returned evidence against the invocation character budget. An overflow cancels that invocation and leaves original sources intact; choose smaller excerpts or increase the declared capacity before retrying.

Summary results include seven classified memory sections, parent summary IDs, and original-source SHA-256 hashes alongside the paged source identities. Hashes identify the content used, not the semantic accuracy of a model summary. Historical summaries may have no classified sections. Use the original turn tools to verify disputed claims.

`get_summary` returns a bounded text excerpt (default 2048 UTF-16 characters). Follow `nextContentOffset` using the returned summary `id` and `contentOffset`; `length` is limited to 8192. Its separate `offset` must be a multiple of 100 and selects the original-source hash page. Responses avoid duplicating the full structured memory in each excerpt. REST summary inspection still returns the complete rendered summary and typed sections.
