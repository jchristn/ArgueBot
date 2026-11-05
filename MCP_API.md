# Model conversation tools

Protocol 2 exposes evidence tools to the model serving the current participant or orchestrator. The service binds each invocation to its run, seat, tenant, accepted history boundary and context epoch. Tool input cannot select another tenant, conversation, run or identity.

The tool server is invocation-scoped and needs no bearer token. Claude Code and Codex reach it through a per-invocation stdio MCP bridge that talks to the service over a local pipe, not over HTTP, so it never sees a session or credential token and cannot be reached with one. It reads only the run's own conversation, inside the run's tenant.

No identity, credential, secret, share or other management operation is exposed. In particular, the member-only secret reveal operation (`POST /v2.0/api/configuration/secrets/{id}/reveal`) is deliberately absent from this tool surface, so a model can never read a stored secret, create a share link or learn about users, tenants or tokens.

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

## Native MCP transport

The service creates an owner-only named pipe for one invocation and launches its own executable with `--context-mcp PIPE` through the harness's MCP configuration. This mode uses stdin/stdout only for newline-delimited UTF-8 JSON-RPC. It does not open a database, attach a workspace or launch an HTTP management listener. The pipe disappears when the invocation ends. The command is an implementation detail, not a user-facing launcher.

Supported methods are `initialize`, `ping`, `tools/list` and `tools/call`. Initialization negotiates the supported 2024-11-05, 2025-03-26, 2025-06-18 or 2025-11-25 protocol revision. The server advertises only the supplied conversation tools. Duplicate request IDs are rejected before executing a tool. Each connection is bounded to 256 frames, 32 tool executions and 128 KiB per frame. Cancellation closes the pipe and terminates the harness process tree through the existing CLI runner.

Claude Code uses `--strict-mcp-config --mcp-config` with the generated server definition. Codex uses `--config mcp_servers.arguebot.command=...` and `--config mcp_servers.arguebot.args=...`. No pasted token appears in these arguments. Local fixture child processes exercise both configuration shapes, protocol negotiation, original evidence retrieval and rejection of duplicate/unknown calls. Live provider compatibility remains a separate acceptance gate, including the pinned Docker CLI versions.

References: [MCP stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports) and [Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## Generic CLI request envelope

The service appends tool definitions and a response contract to the harness prompt. A tool-capable completion returns a JSON envelope instead of final answer text; the service executes the bounded calls and starts a continuation with their results. This adapter may invoke the harness more than once for one debate turn. It does not preserve an interactive CLI session. For example, `{"contextTool":{"name":"get_turn","argumentsJson":"{\"id\":\"msg_example\"}"}}` requests one source. Any response without a valid `contextTool` envelope is treated as final answer text and is still subject to the debate phase's structured-output validation. The adapter permits at most sixteen continuations. Native MCP should be preferred when the harness supports it.

Conversation and local tools share the run-level tool-call budget, in addition to their per-invocation transport bounds. Conversation retrieval never grants permission to run a process. REST local tools are available only during participant responses when the service's local-execution setting permits them. Existing CLI harness execution policies still apply to their built-in tools.

Native MCP also accounts for prompt/tool definitions and cumulative returned evidence against the invocation character budget. An overflow cancels that invocation and leaves original sources intact; choose smaller excerpts or increase the declared capacity before retrying.

Summary results include seven classified memory sections, parent summary IDs, and original-source SHA-256 hashes alongside the paged source identities. Hashes identify the content used, not the semantic accuracy of a model summary. Historical summaries may have no classified sections. Use the original turn tools to verify disputed claims.

`get_summary` returns a bounded text excerpt (default 2048 UTF-16 characters). Follow `nextContentOffset` using the returned summary `id` and `contentOffset`; `length` is limited to 8192. Its separate `offset` must be a multiple of 100 and selects the original-source hash page. Responses avoid duplicating the full structured memory in each excerpt. REST summary inspection still returns the complete rendered summary and typed sections.
