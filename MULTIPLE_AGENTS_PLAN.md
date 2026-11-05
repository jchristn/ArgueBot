# ArgueBot v2.0.0: debates with multiple agents

Status: product plan finalized with moderator decisions; technical proposals remain subject to implementation validation. Target: v2.0.0. Branch: `feature/v2.0.0`. Prepared November 3, 2026. Implementation and verification are in progress; the gates below distinguish local evidence from release acceptance.

## 1. Product outcome

A moderator should be able to say, "I want Claude, Codex, Gemini, and DeepSeek to duke it out," choose the configured agents, and start a debate. DeepSeek can run behind an Ollama or OpenAI-compatible endpoint; it does not require a dedicated DeepSeek adapter. ArgueBot should ask pointed questions, make disagreements explicit, and help the participants develop a proposal they can all evaluate. The moderator can supply new facts or change direction throughout the debate.

The central product promise is a debate whose reasoning remains inspectable as the panel and history grow. Every answer identifies its speaker and the context it received. Summaries make long conversations manageable, while original turns and supporting evidence remain available. Consensus identifies who agreed with which proposal and under what assumptions. Agreement is an outcome of the discussion, not proof that the conclusion is correct.

The supported experience includes the C# service, React dashboard, C# TUI, scriptable C# CLI, shared native client, REST API and API Explorer. Docker must support the same workspace and provider configuration, including pasted Claude Code tokens and Codex `auth.json` credentials collected with the respective CLIs.

### Scope and boundaries

- Support an ordered collection of 2 through n debate participants. Keep single-agent follow-ups. Remove the current special treatment of a third speaker.
- Make harnesses, endpoints, models, credentials, participant profiles and the orchestrator configuration reusable resources.
- Use a selected model for orchestration questions and context compaction. Keep scheduling, validation, persistence, cancellation and consensus enforcement in deterministic service code.
- Provide model-callable tools for retrieving prior turns, evidence, unresolved questions and current proposals, plus requesting clarification from the moderator.
- Preserve original transcripts, private-directive delivery boundaries, historical configuration snapshots and explicit recovery after uncertain external execution.
- Keep one open workspace. Provider credentials do not introduce product accounts, roles, login, pairing, authorization screens or security audit features.
- Keep SQLite and PostgreSQL, service ownership of all database writes, built-in request history and ordinary local logs. Do not restore removed database providers, backup/restore workflows, external monitoring stacks or retired interfaces.

## 2. Decisions and assumptions

The moderator confirmed the first three decisions below: unanimous agreement, roster changes during a debate, and a delivery choice on every interjection. These are v2.0.0 requirements. The remaining technical proposals establish an implementation baseline and will be validated through the delivery gates.

| Decision | v2.0.0 behavior | Status and implication |
| --- | --- | --- |
| What counts as consensus? | Explicit unanimous agreement from all active participants on one proposal revision and context epoch. | Confirmed. Every active agent must agree; majority voting and moderator-declared consensus are outside v2.0.0 scope. Preserve dissent when limits are reached. |
| Can the panel change? | Add, remove or replace participants at a durable turn boundary. | Confirmed. Roster changes are required; each change creates a roster revision and requires fresh agreement. |
| When does an interjection take effect? | For every interjection, the moderator chooses Interrupt now or Use in next turn. Persist immediately in both cases. | Confirmed. Store and honor the choice per submission; already-running answers cannot retroactively receive context. |
| How do agents take turns? | Sequential turns with a rotating first speaker and model-proposed targeted questions. | Draft design. Parallel response batches are a later extension, not necessary to support n participants. |
| How large is n? | Configurable service admission limit, initially proposed as 16 active debate seats; architecture and contracts use collections with no fixed speaker slots. | Validate at 2, 3, 4, 8 and 16, plus 32 with a raised limit. The limit is operational configuration, not a claim of unlimited capacity. |
| Which model orchestrates? | Required explicit orchestrator profile; reuse its model for questions and summaries. | No hard-coded vendor or automatic use of the first participant. An optional separate summarizer profile can follow later. |
| What happens when a model fails? | Pause affected work with a specific reason and explicit retry, replacement or stop controls. | Preserve the existing prohibition on silently replaying uncertain external work. |
| Does this change runtime versions now? | No runtime/package changes in this planning task. README and Unreleased changelog identify the v2.0.0 target. | Product version synchronization belongs to release preparation; no release or prerelease is implied by the branch name. |

A configured maximum must be discoverable through service capabilities so clients can show it before submission. Lowering it must not corrupt existing runs. Existing runs may finish under their accepted limit; new or expanded rosters must satisfy the current admission policy. At least two active seats are required to continue a debate. A failed or removed participant must not silently disappear from the consensus denominator.

## 3. Current implementation and requirements baseline

The current product already supports two or three participants. `src/ArgueBot.Core/Database/Implementations/RunMethods.cs` enforces that limit, and `src/ArgueBot.Tui/DebateSetupEditor.cs` presents first, second and optional third choices. `CreateRunRequest` already has a participant ID list; `DebateRun` stores immutable runner snapshots and derives follow-up mode from participant count. The generalized design should reuse that foundation while making run mode explicit so roster changes cannot reinterpret a debate as a follow-up.

`ConversationEngine.cs` advances a speaker index and currently accepts a completed answer beginning with `WE HAVE CONSENSUS` as consensus. Existing notes and recipient directives provide useful starting points for interjections. They do not establish that every agent has acknowledged a new constraint or the same final proposal.

`docs/EXECUTION.md` describes exact ordered source IDs, prompt digests, durable generation attempts, partial-answer exclusion, bounded history, event replay and explicit uncertain-attempt recovery. Context is currently bounded by characters and message count; an oversized prompt pauses instead of silently truncating. REST models already have local tools through PolyPrompt. Those tools and their execution policy must remain distinct from read-only conversation retrieval.

The implemented API source of truth is `assets/api-contract.json` plus named C# DTOs. `scripts/generate-api.mjs` generates `REST_API.md`, `assets/postman/openapi.json`, `assets/postman/arguebot.postman_collection.json` and `assets/transport.json`. Preserve that generation direction. Do not hand-edit generated artifacts into a competing contract or advertise proposed routes as implemented.

The repository had unrelated uncommitted runtime, dashboard, test and documentation work when this branch was created. Preserve that work and reconcile subsequent implementation against the actual merged baseline, especially generation diagnostics, cancellation and Claude stream parsing.

### Requirements reconciliation gate

Apply `AGENTS.md`, `ROADMAP.md` and `docs/REQUIREMENTS.md` with their explicit scope overrides. Normative sources live in `~/Code/Agents/requirements`; reviewed hashes are in `docs/requirements-manifest.json`. A read-only hash comparison during planning found changes in `BACKEND_ARCHITECTURE.md`, `BACKEND_TEST_ARCHITECTURE.md` and `REPOSITORY_REQUIREMENTS.md`. The other listed source hashes matched. Do not refresh the manifest merely to remove the discrepancy.

Before runtime work, review those three changes, record applicable deltas and conflicts, and update traceability only after review. Topics include structured relational storage, test-framework/Touchstone expectations, full platform and provider CI, AOT applicability and Docker repository requirements. Existing xUnit/NUnit/console adapters remain the current baseline until any required migration is explicitly scoped. The open-workspace and two-database overrides continue to supersede generic identity and four-provider requirements.

All C# implementation must retain block namespaces, alphabetized inside-namespace usings, explicit types, one type per file, `_PascalCase` private fields, XML documentation on public APIs, cancellation tokens, `ConfigureAwait(false)` in server/library code and classic using blocks. Keep nullable analysis, warnings-as-errors and deterministic builds. Shared tests and libraries do not write to the console. New material contains no em-dashes, secrets or real user homes in fixtures.

## 4. Moderator experience

### Configure the panel

The moderator first creates reusable connections and agents. A guided form asks whether the agent uses a CLI harness or HTTP endpoint, then shows only the relevant fields. Presets cover Claude Code, Codex, Gemini CLI, Mux and generic CLI, as well as Ollama and OpenAI-compatible HTTP. Existing Anthropic and Gemini REST profiles remain supported through the current adapters.

A four-agent setup can contain Claude Code with a stored CLI token, Codex with a stored CLI login, Gemini CLI with its supported credential configuration, and a DeepSeek model exposed by the moderator's Ollama server. The orchestrator is selected separately and may use any configured compatible model. Reusing a model for an orchestrator does not make the orchestrator an additional voting participant.

Configuration shows credential status, endpoint reachability, declared and verified capabilities, context capacity and last checked harness version. Saving a form performs local validation only. Connectivity, model discovery and a test completion are separate actions with visible descriptions of what they contact and whether they invoke a model. Manual model IDs remain supported when discovery is unavailable.

### Start and guide a debate

The debate wizard collects the question, participant roster, optional participant stance/instructions, orchestrator, round and generation limits, context budget and the unanimous-consensus requirement. Interjection timing is chosen per submission in the composer. Review shows the final roster, execution locations, configured models, credential readiness and estimated workload. A maximum of r full rounds with n participants means up to n × r participant turns, plus separately bounded orchestration, summaries and tool continuations; it is not a token or price guarantee.

The workspace has a persistent composer labeled Add context. It remains usable while an agent streams, while ArgueBot is asking a question or summarizing, during intervention countdowns, and while paused or reconnecting. Every submission presents an explicit choice between Interrupt now and Use in next turn; a run-wide setting cannot remove that choice. Drafts preserve both text and the selected delivery mode across navigation. Submitting shows a durable receipt, then a status such as Queued for next turn, Applied to question 3, or Seen by 2 of 4 agents. Disconnected clients keep a local draft and cannot claim server acceptance until a receipt arrives.

The moderator may pin a constraint, correct an earlier fact, target a private directive, answer an agent's clarification request, pause, interrupt, stop, extend the debate or add, remove or replace agents at a safe turn boundary. The UI distinguishes a public interjection from a directive delivered only to selected recipients. A private directive is a model-context delivery rule inside an open workspace, not a promise of secrecy from other human workspace clients.

### Review the outcome

The final view presents the exact proposal, each participant's position, assumptions, evidence references, unresolved questions, dissent, rounds completed and the reason execution ended. Outcomes distinguish consensus, no consensus at the limit, moderator stopped, blocked by failure and interrupted work. A synthesis written by the orchestrator cannot substitute for participant agreement.

After completion, the moderator can ask a single participant a follow-up or start a continuation debate. Context submitted after completion remains in the conversation but does not reopen a run or incur model cost automatically. A continuation snapshots the revised configuration and retains links to earlier proposals and outcomes. Markdown/JSONL exports include roster changes, moderator contributions, summaries and their provenance, agreement records and the final result.

## 5. Debate protocol and orchestration

### Service-owned state machine

Persist run lifecycle separately from debate phase. Lifecycle states cover queued, running, paused, completed, stopped and failed; phase records opening, questioning, responding, synthesizing, ratifying or compacting. Exact names must be finalized in the contract ADR. Execution remains in the service's existing worker and exclusive database ownership model. Clients submit commands and observe committed events.

At a normal round boundary, ArgueBot asks its configured model to identify the most important disagreement and propose pointed follow-up questions. Questions may challenge an assumption, request evidence, ask what would change a position or compare a concrete tradeoff. They should lead the discussion toward an answer without supplying a preferred conclusion. Opening positions and periodic full-panel rounds ensure everyone speaks; targeted questions cannot starve quieter agents.

The model returns a typed orchestration decision with question text, intended seats, referenced claims/turns, rationale suitable for display, and a proposed next phase. Service code validates recipients, source visibility, budgets and legal transitions. It chooses the actual schedule and persists the accepted decision. Models cannot grant themselves additional rounds, change the roster, execute arbitrary service commands or declare consensus. Store the displayed rationale, not hidden chain-of-thought.

Track participant-turn, orchestration-call, compaction-call, tool-call, wall-clock and optional token/cost budgets independently. Defaults and maxima are disclosed in setup and capabilities. Token and cost estimates identify their source and uncertainty. A hard currency cap is available only when pricing and accounting can enforce it; otherwise offer generation/token/time limits with an estimated cost display. Model latency must not hold a database transaction or block moderator commands.

### Consensus protocol

An orchestrator-produced candidate becomes an immutable proposal revision with a content digest, roster revision and context epoch. Each active seat receives that exact proposal and responds with `agree`, `disagree`, `abstain` or `needs_information`, plus a brief rationale and evidence references. Unanimity requires explicit `agree` from every active seat. Missing responses, abstentions, errors and quoted consensus phrases do not count.

Any proposal edit, applicable moderator interjection or roster change invalidates prior agreement for the current decision. The service atomically checks the expected run revision, proposal revision, roster revision and context epoch when recording a final outcome. If a new interjection commits first, finalization must fail its stale check and schedule reconsideration. If finalization commits first, the interjection is retained as post-run context and the user sees the continuation action.

Agents without a native structured-output feature may use a documented, bounded response envelope validated by their adapter. Invalid or ambiguous agreement is not inferred from prose. Permit at most one configured repair attempt within the orchestration budget, then pause with a useful explanation. Preserve existing historical consensus records as legacy agent-declared outcomes; do not fabricate votes during migration.

### Scheduling, roster changes and recovery

Each run has stable participant-seat IDs, distinct from reusable profile IDs and display names. A seat snapshots its harness/model settings and instructions. The same underlying model can occupy different explicitly configured seats; accidental duplicate selection is rejected or requires deliberate duplication with distinct names. All progress and consensus records address seat IDs.

Apply roster commands at the next safe boundary with optimistic revisions. Adds receive a catch-up packet and must respond before ratification. Removal is explicit and recorded, including prior dissent; replacement creates a new seat and closes the old membership interval. Never rewrite earlier turns or silently count a replacement's agreement as its predecessor's. Freeze round membership when a round opens; additions join the next round, and pending removals close at the boundary. A removal that would leave fewer than two active seats is refused with guidance to replace the seat or stop the debate (`minimum_roster`). This implementation decision replaces the earlier pause proposal, because it never leaves a run that cannot continue.

Sequential execution is the initial v2 policy because it makes context visibility and existing local-tool side effects understandable. Rotate opening order across full rounds and bound targeted turns between them. Global run concurrency and per-endpoint capacity still matter across conversations. Queue fairly, expose waiting status, and stop scheduling when provider limits or service budgets are reached. Parallel batches require a separate future design for a shared context snapshot, deterministic merge order and conflicting local writes.

Cancellation flows from service commands through orchestration, summarization, model streams, tool continuations and child process trees. Pause lets current work finish; interrupt cancels the current invocation; stop prevents future invocations. Late output from a cancelled attempt cannot advance a checkpoint or vote. Restart pauses uncertain work and records the affected phase. Explicit retry creates a new attempt, links the prior one and explains possible duplicate external effects or provider charges.

## 6. Interjections and context epochs

Every interjection is an append-only record with command key, sequence, creation time, scope, intended seats, optional superseded record, required `deliveryMode` and application status. The allowed modes are `interrupt_now` (Interrupt now) and `next_turn` (Use in next turn). Reject missing or unknown modes instead of silently choosing for the moderator. Return the accepted mode in the durable receipt and history. Accept it without waiting for the active model call. Its durable receipt identifies the run/context epoch to which it applies. Concurrent clients use idempotency keys and expected revisions, with conflicts surfaced instead of overwriting edits.

At preparation time, each generation freezes its context epoch and selected sources. Completed answers from an earlier epoch remain visible but are labeled as preceding the new context. Public context applies to all subsequent participant and orchestration work; recipient directives stay in their permitted partitions. Pinning guarantees inclusion of the source text subject to the hard input budget, not silent priority over the budget. If pinned material alone cannot fit, pause and ask the moderator to revise it or choose a larger-capacity model.

Use in next turn lets the current invocation finish and includes the interjection before the next eligible invocation is prepared. Interrupt now first persists the interjection, then requests cancellation, and shows cancellation as pending until acknowledged. It must not lose the new text if cancellation fails. With no active invocation, either mode queues the context for the next eligible invocation without starting or resuming work automatically. An interrupted invocation follows the existing explicit-retry recovery rule; the UI explains that choice and possible repeated side effects. Multiple interjections retain their own delivery modes and sequence order; one submission cannot change another's mode. A summary finishing against an older epoch is kept as historical evidence but cannot be activated for the newer epoch without validation. Corrections supersede earlier context explicitly while preserving both records. Per-seat delivery records make it possible to tell whether every agent has addressed the new fact before the next ratification.

## 7. Context memory, compaction and model tools

### Memory with inspectable sources

Keep the complete original transcript as the authoritative record. Build each prompt from immutable instructions, the current question, pinned constraints, the current proposal, relevant recent turns, a validated summary and selected evidence. Selection records inclusion/exclusion reasons and the exact source order, not just a message count. Respect recipient boundaries before selecting, summarizing, searching or returning tool results.

Each summary records model/profile snapshot, prompt version and digest, source IDs and sequence range, source content hashes, visibility partition, covered context epoch, parent summary IDs, token estimate/usage, generation attempt and validation status. Its structured content separates agreed facts, contested claims, participant positions, evidence, decisions, unanswered questions and moderator constraints. Reference original turns even when compacting a previous summary, and periodically rebuild from original evidence to limit cumulative distortion.

A recipient-only directive must never leak through a shared summary or the orchestrator's public question. Shared orchestration sees public material. If private content needs compaction, produce a separate recipient partition and keep it out of shared synthesis. Conversation tools enforce the same boundaries. Untrusted quoted turns and tool results cannot change system instructions or authorize new tools.

Use the orchestrator's selected model to compact context when the next prompt approaches its configured capacity. Reserve room for output, tool definitions and bounded tool results. Prefer verified tokenizer/counting support; otherwise label estimates, retain the existing character/byte safety bounds and handle provider context rejection explicitly. The summarization request must itself fit its model's capacity: use bounded chunks with source manifests and a bounded merge pass.

Validate schema, source existence, source visibility, required pinned references and proposal/constraint consistency before activating a summary. Mechanical validation cannot prove semantic faithfulness, so test with evidence-rich reference conversations and expose View sources, Exclude summary and Rebuild summary controls. A failed, cancelled, stale or oversized summary leaves the last valid context intact and pauses if no valid prompt fits. Never delete original messages or claim compaction guarantees that nothing can be forgotten.

### Tool contract

Expose the following tools through one typed service implementation. REST runners use provider-native tool calling. CLI harnesses use a documented run-scoped stdio MCP bridge where supported. Generic harnesses must declare bridge support or use an explicitly documented bounded request-envelope adapter; do not silently present a completion-only harness as tool-capable.

| Tool | Inputs | Result and boundary |
| --- | --- | --- |
| `list_turns` | Cursor, sequence boundary, optional seat/phase filter, page size | Bounded visible turn metadata with stable next cursor. |
| `get_turn` | Turn ID, optional bounded content offset | Original visible content and source identity; continuation metadata when too large. |
| `search_context` | Query, allowed filters, result limit | Visible snippets with turn/evidence IDs; no hidden partition counts or snippets. |
| `get_debate_state` | Optional known revision | Current roster, phase, constraints, proposal and recorded positions for the invocation's scope. |
| `get_supporting_details` | Claim/evidence IDs, cursor | Linked supplied evidence, attribution and known gaps; no invented citations. |
| `get_summary` | Summary ID or current summary selector | Summary, provenance and accessible original-source references. |
| `request_clarification` | Question, blocking flag, relevant source IDs | Durable moderator request and status. Repeated calls use a stable call ID. |

Conversation/run/seat identity comes from the invocation context, not model-supplied arbitrary IDs. Reject cross-run or out-of-scope access. Initial support for evidence means moderator-supplied text and links plus retained visible tool results; automatic web crawling, embeddings and a separate vector database are not required. Retrieval should work over relational data and existing search facilities first.

Read tools are available even when local file/process execution is disabled. Their limits cover query length, result count, bytes, total returned tokens, time and calls per turn. Existing `read_file`, `write_file`, `list_directory` and `run_process` remain governed by the service local-execution switch. Only explicit moderator action changes that policy.

Record tool calls/results in the generation history with correlation IDs, duration, selected sources and safe error codes. Enforce cancellation, duplicate-call handling, argument validation and bounded loops. A blocking clarification pauses at a checkpoint and resumes when the moderator answers or explicitly skips it; a nonblocking request stays visible while the debate continues. Model text cannot answer a moderator request on the moderator's behalf.

The MCP bridge must not expose the open-workspace management API wholesale to a model. Use process-bound stdio context and narrowly scoped service dispatch, with no product login or user accounts. Publish its schema and examples in `MCP_API.md`; verify exact integration flags against pinned harness versions.

## 8. Harnesses, endpoints, models and credentials

### Reusable configuration

Separate configuration by what the moderator is changing. A participant is a named debating role, a harness defines how a process runs, an endpoint defines an HTTP connection, and a model profile describes a model's usable capabilities. Existing combined `RunnerProfile` records migrate into these resources with stable links and compatible read views.

| Resource | Required responsibilities |
| --- | --- |
| Harness profile | Adapter/protocol, executable, literal argument rows, supported model flags, working directory, environment allowlist, credential injection bindings, tool bridge and completion parsing. |
| Endpoint profile | Protocol, normalized base URL, optional explicit path configuration, auth bindings, safe extra headers, connection limits, timeout and enabled state. |
| Model profile | Endpoint or harness reference, provider model ID, context/output limits, generation settings, declared/verified streaming, tools and structured-response capabilities. |
| Credential | Kind, name, encrypted secret value or environment/file reference, revision, status, safe expiry metadata and last validation time. Values are write-only. |
| Participant profile | Display name, model/harness binding, role instructions, tool policy and per-turn limits. |
| Orchestrator configuration | Model profile, question/synthesis prompts, schema versions, compaction policy, budgets and supported capabilities. |

Snapshots freeze nonsecret configuration, resource revisions and credential references. Never embed plaintext tokens in run snapshots or prompt provenance. Credential rotation updates the reference's usable value for future invocations; record the credential revision used without retaining its secret in the run. Disabling or deleting an in-use configuration prevents new runs and explains effects on resumable work. Historical metadata remains readable. Changed endpoint/model/harness settings require an explicit replacement or new run rather than quietly changing an accepted snapshot.

### HTTP transports and authentication

Support native Ollama chat and OpenAI-compatible Chat Completions as explicit protocols. Preserve streaming, cancellation, tool-call IDs, tool-result continuation, completion reasons and usage when supplied. OpenAI Responses support is a separate capability, not something implied by the words OpenAI-compatible. Inspect PolyPrompt's actual transport/auth extension points before choosing whether to extend it or add a narrow transport adapter. Do not replace working provider support without regression evidence.

| Authentication choice | Configurable representation |
| --- | --- |
| None | Explicitly selected, useful for a local Ollama server. |
| Bearer token | `Authorization: Bearer` with a stored credential/reference. |
| API key | Secret plus explicit header name, with provider-specific presets rather than an assumed universal header. |
| HTTP header key/value | One or more named header bindings, distinguishing secret references from ordinary static values. |
| Query string | Named query parameter with a secret reference and correct URI encoding. |

Support gateways that require more than one binding, such as bearer auth plus a routing header. Reject conflicting duplicate header/query keys, CR/LF injection, unsafe protocol headers and ambiguous authorization overrides. Keep secret values out of the saved base URL. Apply the same intended bindings to discovery, validation and generation, with explicit per-operation overrides only where needed. Cross-origin redirects must not forward credentials; TLS validation stays enabled, with explicit trust configuration for private certificates.

The configuration preview shows method, normalized destination and redacted header/query names. Requests execute only on the service. Query credentials require redaction before URL logging, request-history capture, exceptions, telemetry fields, exports and browser display. Sensitive request bodies, including credential creation and replacement, must be excluded from capture before any value is stored. Test that provider errors echoing a key are scrubbed too.

Ollama's native chat endpoint accepts tool definitions, and Ollama's OpenAI interface implements a subset of the OpenAI API. Local Ollama does not require authentication, while direct Ollama cloud access uses a bearer API key. These are capability inputs, not guarantees about every model or gateway. See the official [Ollama chat contract](https://docs.ollama.com/api/chat), [compatibility documentation](https://docs.ollama.com/api/openai-compatibility) and [authentication documentation](https://docs.ollama.com/api/authentication). OpenAI's own API uses bearer API keys; custom compatible endpoints may require different bindings. See [OpenAI API authentication](https://developers.openai.com/api/reference/overview).

### Pasted CLI credentials and Docker

Use Garrison as an implementation reference for credential handling, without importing its identity system or cluster architecture. Relevant local sources in the sibling repository are `../Garrison/docs/QUICKSTART.md` (Store the credentials), `../Garrison/docs/DOCKER.md`, `../Garrison/docs/SECURITY.md` (Codex logins), `../Garrison/src/Garrison.Core/Codex/CodexAuthDocument.cs`, `../Garrison/src/Garrison.Server/Runtime/CodexCredentialService.cs` and the `claude-code.json`/`codex.json` harness profiles under its seed directory.

Provide a masked multiline paste/import control for Claude Code OAuth tokens obtained through `claude setup-token`, and for complete Codex `auth.json` documents obtained through CLI login. The CLI accepts stdin or `--file`, never requires the token on the command line. Keep API-key and CLI-login credential types distinct. Validate a Codex document before replacing a valid stored value, preserve supported unknown fields where required, and return field errors without echoing the submitted document.

For Claude Code, inject the stored token as `CLAUDE_CODE_OAUTH_TOKEN` only into the selected harness process. For Codex, materialize the appropriate `auth.json` in a private per-invocation configuration home, with restrictive file permissions and cleanup. Login instructions use a dedicated disposable `CODEX_HOME`; they do not copy an actively shared personal login by default. Never mount an entire host home to make authentication work.

Garrison centralizes Codex refresh and gives runs a sanitized credential copy. Adapt that ownership principle: the service serializes refresh by credential ID, writes rotated secrets atomically with revision checks, and distinguishes transient refresh failures from Needs reauthentication. A refresh timeout after possible provider rotation is uncertain and must not blindly replay the old refresh token. Runs must not race to refresh one shared login. Verify the supported Codex version accepts the sanitized run document and configured refresh behavior before adopting Garrison's exact flags. Failure of that compatibility check is a release blocker for this credential mode, not a reason to expose the master refresh token to every process.

Encrypt pasted values at rest using authenticated encryption and a versioned master key loaded from an OS-protected location or mounted Docker secret, kept separate from the database. Define key initialization, permissions, rotation and restart behavior. Missing or incorrect keys fail clearly without overwriting credentials. Imported environment/file references remain available for operators who prefer external secret management. Treat stored credentials and all token fields as redaction inputs, including runner diagnostics and temporary-file paths.

Build an ArgueBot service/dashboard image and Compose configuration with persistent application data, a separate key secret, non-root execution, explicit working-directory mounts, installed supported harnesses and health checks. Support optional Ollama in Compose and an external Ollama/OpenAI-compatible endpoint. Explain that `127.0.0.1` inside a container addresses that container; use Compose service names or documented host-gateway configuration for external model services. Test Linux host connectivity as well as Docker Desktop.

Publish the service port on host loopback by default. Keep the open-workspace trust model clear: credential secrecy at rest does not create a multi-user authorization boundary, and local tools/harnesses operate with service/container permissions. Limit per-process environment inheritance, avoid Docker socket mounts, exclude secret files from build contexts, and do not bake credentials into image layers, Compose files or sample `.env` content. Restarts retain stored configuration and credential usability without requiring another paste, unless reauthentication is actually needed.

## 9. Backend and persistence work

Add domain-specific interfaces and typed records for configuration, credentials, roster membership, orchestrator decisions, context manifests, summaries, interjections, proposals, agreements and clarification requests. Use normalized tables and indexed child rows for stable structured data. Versioned provider payloads may retain bounded opaque details where necessary, but the relational model must support querying seats, sources, revisions and outcomes directly.

| Area | Implementation work and invariant |
| --- | --- |
| Core models | Explicit run mode; ordered seat collection; orchestration settings; phase/epoch/revision fields; typed validation errors. |
| Database | SQLite and PostgreSQL schema/migrations, PrettyId identifiers, fixed workspace scope and domain-specific methods. Index run/sequence, seat/round, proposal/seat and summary/source lookups. |
| Engine | Replace fixed roster validation and phrase-based consensus; separate orchestration decisions from scheduling and finalization. |
| Worker | Fair queues, per-endpoint limits, cancellation and durable restart handling for all invocation purposes. |
| Runners | Capability negotiation, endpoint auth, credential injection, structured envelopes and conversation-tool bridge. |
| Context | Recipient-aware source selection, chunked summaries, provenance, validation, tool retrieval and budget enforcement. |
| Server | Feature-specific Watson routes, typed request/response DTOs, redaction before capture and durable receipt/event delivery. |
| Native client | Typed operations and event handling for every new resource and command, including conflict and replay-gap recovery. |

Commit accepted commands, events, pending attempts and checkpoints atomically where the current engine already requires it. Do not hold transactions across HTTP calls, child process execution or model tool requests. Claiming work, completing a turn, recording a vote and advancing a phase must be idempotent under retries and ownership checks. All readers use bounded pages; no UI requires loading the entire debate into memory.

Migrations preserve old two/three-agent runs, existing single-agent follow-ups, source provenance, message content, credential environment references and original outcomes. New tables can represent historical snapshots without rewriting their content. Never infer a missing orchestrator model from the first participant: old active debates either continue through a documented legacy path or pause with an explicit conversion workflow. Conversion creates a linked v2 run with a selected orchestrator and fresh ratification state.

Exercise migration failure and internal upgrade safeguards on both databases. Define the minimum readable schema and make older binaries refuse newer incompatible schemas rather than partially running. A downgrade procedure must state when an older binary cannot read a migrated database; preserve the existing internal recovery mechanisms without adding a user-facing backup/restore feature.

## 10. Proposed API contract

Document the versioning decision in an ADR before implementation. The proposed default is `/v2.0/api` for the new semantics, with a compatibility layer for documented `/v1.0/api` reads and existing workflows during transition. The product version and the service transport protocol number are separate. New clients negotiate capabilities at attachment; old clients must receive an actionable incompatibility response before controlling a run whose semantics they cannot represent.

Paths below are design targets, not implemented endpoints. Reuse existing naming conventions where practical and update the canonical contract, DTOs, native client and generated outputs together. All management routes stay in the open workspace. Provider authorization belongs only on outbound provider calls.

| Proposed resource or operation under `/v2.0/api` | Contract requirements |
| --- | --- |
| `GET capabilities` | API/protocol versions, supported adapters/auth bindings/tools, resource limits and explicit compatibility flags. |
| `harnesses`, `endpoints`, `models`, `participants` | GET collection/item, POST create, PUT update and DELETE with dependency checks; bounded paging and expected revision for mutation. |
| `POST endpoints/{id}/validate`, `GET endpoints/{id}/models`, `POST models/{id}/test` | Distinguish local checks, network discovery and explicit inference; safe diagnostics, cancellation and usage reporting. |
| `credentials`, `credentials/{id}/value` | Metadata list/read/create/update/delete; write-only value creation/replacement, no secret read endpoint. Dependency and revision checks. |
| `orchestrator-profiles` | Reusable orchestration/summary settings, validation and references to model profiles. |
| `POST conversations/{id}/runs` | Ordered seats, question, orchestrator, budgets and fixed unanimous-consensus semantics; durable receipt with run ID and revision. |
| `GET conversations/{id}/runs/{runId}` | Lifecycle, phase, roster revision, context epoch, active attempt and progress. |
| `POST conversations/{id}/runs/{runId}/commands` | Typed pause/resume/interrupt/stop/extend, roster-change and continuation commands with expected revision and idempotency key. |
| `POST conversations/{id}/interjections` | Durable shared/recipient context, optional correction/pin/request linkage and required `deliveryMode`: `interrupt_now` or `next_turn`; receipt echoes the mode. |
| `GET conversations/{id}/interjections` | Bounded records with application/delivery status per seat. |
| `GET conversations/{id}/runs/{runId}/context` | Prepared manifest and inclusion reasons for a specified attempt, with source reads through existing bounded history. |
| `GET conversations/{id}/summaries`, `POST conversations/{id}/summaries/rebuild` | Bounded provenance reads and explicit model-backed rebuild receipt; do not rewrite old summaries. |
| `GET conversations/{id}/runs/{runId}/proposals` | Proposal revisions, visible positions, agreement records and source references. |
| `GET conversations/{id}/clarifications`, `POST conversations/{id}/clarifications/{requestId}/answer` | Durable requests and moderator answers or explicit skip. |
| Existing messages, attempts, events and exports | Extend typed shapes to include seats, phase, epoch, summaries and tool traces while preserving bounded history semantics. |

Management create returns 201; accepted asynchronous execution returns 202 with a receipt, status resource and current revision. Define replay semantics for an identical command key and reject reusing a key with a different payload. Use 400 for invalid fields, 404 for missing visible resources, 409 for revision/state/dependency conflicts, 413 for size limits, 429 for admission limits and 503 for unavailable service dependencies as appropriate. Provider failures after acceptance appear on attempts and run status, not as a misleading failure of an already accepted HTTP command.

Examples must cover four distinct seats, a nondefault orchestrator, every auth type using secret references, both interjection modes during streaming, a rejected missing/unknown mode, interrupted compaction, stale ratification and reauthentication. Field constraints must be machine-readable: minimum/maximum participants from capabilities, allowed actions/states, nullable fields, enum values, page sizes and maximum text/credential document sizes. Define error codes such as `stale_context_epoch`, `unsupported_model_capability`, `credential_needs_reauthentication` and `summary_validation_failed` with recovery instructions.

SSE events retain stable conversation sequence ordering and replay-gap behavior. Add events for roster changes, orchestration questions, context acceptance/application, summary lifecycle, clarification requests, proposal revisions, agreement records and outcome changes. Large content is referenced by ID, not copied into unbounded event frames. Clients refresh snapshots after a history gap and then resume from a captured head.

## 11. Frontend and surface parity

Every feature must be reachable through the dashboard, terminal and scriptable CLI, with HTTP contracts and native client support. Exact layouts can differ. A terminal workflow should not require opening a browser to import credentials, inspect a summary or recover a failed orchestration call.

| User task | React dashboard | C# TUI | Scriptable CLI / client |
| --- | --- | --- | --- |
| Manage configuration | Participants view plus harness, endpoint, model, credential and orchestrator editors; reusable references and readiness. | Agents/configuration menus with scrollable forms, masked paste/import and contextual help. | Resource commands with JSON stdin/files, redacted output and field-specific errors. |
| Build an n-agent roster | Searchable selection, reorder controls, distinct seat names and capacity display. | Replace first/second/third dropdowns with selectable ordered list and keyboard reorder. | Ordered seat list in typed run request. |
| Follow a debate | Speaker list, current phase/question, streaming transcript, status and limits. | Responsive list/history layout with focusable status and phase text. | Watch events, bounded status/history and machine-readable progress. |
| Interject at any time | Persistent composer with Interrupt now / Use in next turn on every submission, queued/applied status and pin/correction. | Composer available during active work, preserved drafts, explicit scope and delivery-mode choices. | Interject command with required delivery mode, stable idempotency key and receipt/status read. |
| Change roster | Pending-change preview and historical membership. | Boundary-aware add/remove/replace dialogs. | Revisioned roster command and membership output. |
| Inspect memory/evidence | Summary/source drawer, inclusion reasons, original-turn links and rebuild control. | Paged summary/source readers and full-turn navigation. | Context manifests, summary metadata, source reads and exports. |
| Answer clarification | Request list, unread state and answer/skip actions. | Requests menu and inline status without stealing input focus. | List requests; answer/skip by ID. |
| Review consensus | Proposal and per-seat positions with dissent and context currency. | Paged proposal/position view with outcome reason. | Structured outcome plus Markdown/JSONL export. |
| Recover failure | Purpose-specific attempt details, retry/replace/stop and safe error text. | Equivalent diagnostics and choices through existing attempt controls. | Typed error codes and explicit recovery commands. |

Update all current dashboard views: Home summarizes active panels and blocked work; Conversations shows panel size and outcome; Participants manages reusable agents; Workspace handles the debate; Settings exposes limits/defaults; RequestHistory redacts credentials; API Explorer uses the generated v2 contract and safe examples. Preserve copy/export behavior, simultaneous-client updates and historical-run inspection.

Review `ConversationApp`, `DebateSetupEditor`, wizard dialogs, `ParticipantEditor`, attempt readers, `TranscriptFormatter` and `TerminalHelp` for fixed speaker assumptions. Shared localization catalogs must cover plural agent counts, states, delivery labels, secret fields and recovery guidance. Verify keyboard-only operation, focus restoration, screen-reader labels, non-color speaker identification, long names, narrow terminals, mobile-width dashboard layouts, light/dark themes, pseudo-locales and RTL where supported.

Secret fields never repopulate with saved values. An unchanged masked field means keep the existing secret; replace and clear are explicit distinct actions. Avoid persisting credential-entry drafts in browser storage or terminal logs. Validation failures keep ordinary form values while handling submitted secrets deliberately and never reflecting them in error messages.

## 12. Postman, API artifacts and simulation

Extend the current generation pipeline rather than replacing it with a new manually maintained collection. Update `assets/api-contract.json`, DTO documentation and `scripts/generate-api.mjs` together. Generate the OpenAPI document, REST reference, Postman collection and transport metadata, then enforce drift checks in CI. Add support in the generator for any new enum, required-field or nested-type shapes it cannot currently express.

Organize the collection into Capabilities, Configuration, Credentials, Debate lifecycle, Interjections, Context and evidence, Consensus, History and Recovery folders. Document each folder and operation, including whether it can invoke a paid provider. Variables include service base URL, fixture endpoints, model/profile/seat IDs, run ID, expected revisions, context epoch and command keys. Keep all real secret values in local secret variables or the platform's credential store; exported collections and environments contain placeholders only.

Collection assertions must test response schemas and behavior: four accepted seats, both interjection modes and exact delivery, missing/unknown-mode rejection, stale-revision rejection, duplicate-command receipts, no consensus before all votes, explicit dissent, bounded pages, redacted credentials and clear reauthentication. Credential-management requests must use fictional values against isolated homes and must not echo them in test reports. Add a repeatable clean fixture setup and teardown that deletes only resources created by that test run.

Extend `postman/simulations/runners.sim.yaml` and its mocks with Ollama-native and OpenAI-compatible conversations, orchestration decisions, summaries and tool continuations. Exercise the real service through HTTP against these dependencies. Scenarios include delayed output, context rejection, tool loops, malformed structured output, conflicting claims, 401/403, 429 with retry guidance, 5xx, midstream disconnects, cancellation and a credential endpoint returning an invalid refresh response. Local auth fixtures assert exact fictional header/query binding behavior.

Keep the executable collection assertions in a reproducible source location included by generation so regeneration cannot erase them. Preserve the distinction between a mock of ArgueBot's API for UI development and simulations of its provider dependencies for service testing. Use the existing local fixture runner where supported and document the pinned Postman CLI capabilities required for the full workflow.

Planning evidence: Postman CLI 1.75.0 is installed, but `postman status` returns unknown command. No Postman workspace was initialized, rebound or published, and signed-in resource/Context Graph discovery was not attempted. Orbit discovery and its integration brief covered Ollama chat, streaming and tools only partially: model discovery and configurable request authentication were missing from the supplied resource. Official provider documentation remains the contract reference. Tooling compatibility and collection execution must be verified during implementation rather than marked complete here. Hosted publication is a separate release step with a concrete audience and destination.

## 13. Test cases and acceptance evidence

Tests must prove observable behavior through real service boundaries where that behavior depends on persistence, cancellation or provider transport. Deterministic fake models and real fixture subprocesses supply controlled outputs. Tests use disposable application homes, fictional credentials and `127.0.0.1` listeners; database integration uses a disposable PostgreSQL instance. Paid/live compatibility is separately configured and its absence is recorded as skipped evidence.

| ID | Scenario | Required result |
| --- | --- | --- |
| T01 | Create debates with 2, 3, 4, 8 and 16 seats; 32 with increased configured limit. | Every seat receives the documented schedule; no fixed slot assumption or missed turn. |
| T02 | Zero/one-seat debate, over-limit roster, unavailable profile, accidental duplicate or bad orchestrator. | Field-specific rejection before model calls; single-seat follow-up still works. |
| T03 | Four-agent reference journey: Claude, Codex, Gemini and DeepSeek transport fixtures. | Distinct identities, orchestrator questions, tool evidence and explicit agreement records survive reload/export. |
| T04 | One agent says `WE HAVE CONSENSUS`, another quotes it, one abstains. | No unanimous outcome without valid agreement from every active seat. |
| T05 | All seats agree with one revision; then a proposal edit or new constraint races finalization. | Transaction order determines result; stale votes never finalize a newer proposal. |
| T06 | Agent disagrees until round/generation/time limit. | No-consensus outcome with dissent and stop reason; no fabricated agreement. |
| T07 | Use in next turn during participant stream, orchestration, summary, countdown and pause. | Durable receipt echoes `next_turn`; current invocation is not cancelled; next applicable invocation gets the text and epoch; current answer shows old epoch. |
| T08 | Interrupt now during participant output, orchestration, compaction or a local tool, with late output. | Receipt echoes `interrupt_now`; context persists, cancellation propagates and late output cannot advance the run; explicit recovery preserves the new context. |
| T09 | Two clients submit the same key, conflicting edits and an interjection after completion. | One accepted command per key; actionable conflict; post-run context preserved without an automatic new run. |
| T10 | Private directive to one seat followed by summaries and searches from other seats. | No text, snippet, source reference or count leaks through shared context or tools. |
| T11 | Add, remove and replace seats mid-round, including a dissenting seat. | Boundary application, new roster revision, preserved dissent/history and fresh ratification. |
| T12 | Full context exceeds capacity; pinned material alone exceeds capacity. | Valid compaction when possible; explicit pause otherwise; no silent source deletion. |
| T13 | Repeated summaries omit a constraint, invent a citation, fail, cancel or finish stale. | Invalid summary not activated; originals and last valid manifest remain readable. Semantic quality cases have reviewed expected facts. |
| T14 | Context tools page long turns, use invalid IDs, request another run or exceed budgets. | Bounded correct results, typed errors, cancellation and scope enforcement. |
| T15 | REST native tools and CLI MCP/request-envelope retrieval ask for the same evidence. | Equivalent visible results/provenance; local-execution disablement leaves read-only context tools usable. |
| T16 | Blocking and nonblocking clarification, duplicate tool call, moderator answer/skip. | One durable request; correct pause/continue and resume behavior. |
| T17 | Every HTTP auth binding, combined gateway auth, special query characters and redirects. | Exact destination/binding semantics; conflicting/injected headers rejected; secrets not forwarded to another origin. |
| T18 | Secrets in provider errors, request bodies/URLs, process stderr and token subfields. | No secret in API reads, captures, logs, events, exports, reports or committed fixtures. |
| T19 | Invalid pasted auth document, expired login, refresh race, uncertain refresh and token replacement. | Existing valid value preserved on bad input; single refresh owner; explicit recovery; no blind refresh replay. |
| T20 | Docker restart, missing master key, non-root permissions and credential temp-file cleanup. | Persistent values usable when key is correct; clear failure otherwise; no image-layer or world-readable secret. |
| T21 | Crash before/after invocation, tool execution, summary activation and consensus commit. | Durable history, one service writer, no automatic uncertain replay or double advancement. |
| T22 | SQLite/PostgreSQL migration of old debates and historical consensus. | Content and legacy outcomes preserved; explicit conversion and incompatible-binary handling. |
| T23 | SSE disconnect, expired cursor, duplicate event and large streamed content. | Snapshot recovery and correct state across dashboard/TUI/CLI without duplicate turns. |
| T24 | Provider timeout, 401/403, 429, 5xx, missing tool support and malformed output. | Specific attempt purpose/failure and documented recovery; no infinite retries or hidden fallback model. |
| T25 | Global concurrency, per-endpoint limits, slow participant and cancellation storm. | Fair service scheduling, bounded queues and responsive interjection acceptance. |
| T26 | Dashboard, TUI and CLI reference journey plus accessibility variants. | Equivalent completed workflow, correct focus/drafts/help and evidence for supported platforms. |
| T27 | Generated contract, Postman examples/assertions, native client and live API responses. | No schema drift; negative paths and all new operations represented. |
| T28 | Existing CLI parser fixtures, REST local tools, follow-ups, final instructions and diagnostics. | Existing behavior retained; n-agent work does not regress unrelated accepted functionality. |
| T29 | Alternate delivery modes for consecutive interjections from dashboard/TUI/CLI; omit or invent a mode; reuse a command key with a different mode. | Every accepted receipt/history record preserves its own mode; no sticky run-wide policy overrides it; invalid modes and conflicting key reuse are rejected without another side effect. |

Place core/storage/engine/runner checks in `src/Test.Shared` with the supported test adapters, real process behaviors in `src/Test.Fixtures`, component behavior in dashboard tests, and full browser journeys in Playwright. Native TUI journeys must use an isolated real service, alongside rendering/input checks. Extend `scripts/test-cli-surfaces.mjs` for the new command lifecycle. Test cases above are requirements, not claims of tests already implemented.

Run the repository's existing locked restore, Release build, console suite, xUnit, NUnit, CLI launcher checks, dashboard lint/i18n/unit/build/E2E checks and artifact drift checks. Add PostgreSQL, Docker, credential refresh, protocol bridge and Postman jobs as their implementation lands. Reconcile the changed normative test requirements before altering test infrastructure. CI must report missing fixtures and unavailable live-provider coverage explicitly rather than treating skips as compatibility passes.

### Performance and quality gates

Measure n = 2, 4, 8, 16 and configured 32 with increasing transcript/evidence sizes and multiple concurrent conversations. Record environment, commit, database, model fixture latency, first-event latency, interjection acceptance, memory, database growth, prompt sizes, summarization overhead and queue wait time. Keep model latency separate from orchestration overhead. A proposed local-fixture target is p95 interjection acceptance below one second while a model streams; baseline and review hardware-specific thresholds before making them CI gates.

Use a curated debate corpus with contradictory evidence, later corrections, changing assumptions and persistent minority dissent. Review whether questions address the actual disagreement and whether summaries retain required facts and uncertainty. Unit tests cannot prove debate quality or summary fidelity for all models. Release evidence includes human review and an opt-in live four-provider journey with exact harness/model versions and configuration, or explicitly identifies the unverified combination.

## 14. Documentation, packaging and release

README gains a well-written What's new in v2.x section that names v2.0.0 and explains the product in terms of choosing a panel, interjecting context, model-guided questions, recoverable history and reusable connections. During development it must say these are planned capabilities and link here. Once acceptance passes, replace the planning language with verified setup and usage examples. Keep `CHANGELOG.md` under Unreleased with a v2.0.0 planning entry, then add delivered changes as they land.

| Document/artifact | Required updates |
| --- | --- |
| `README.md`, `CHANGELOG.md` | v2.0.0 target, What's new in v2.x, four-agent example, honest delivery status and compatibility notes. |
| `ROADMAP.md`, `docs/REQUIREMENTS.md`, manifest | Link workstreams, reconcile reviewed normative deltas and preserve explicit scope overrides. |
| `docs/EXECUTION.md` | State machine, scheduling, consensus, interjections, context provenance, tools and recovery. |
| `docs/TUI.md`, terminal help | Roster selection, persistent context composer, credentials, memory inspection and failure workflows. |
| `docs/OPERATIONS.md` | Limits, secret/key lifecycle, Docker networking, retention, capacity and reauthentication. |
| `docs/DEVELOPMENT.md` | Fixture setup, Postman workflows, test case mapping, CI commands and recorded limitations. |
| `REST_API.md`, OpenAPI, Postman, API Explorer | Generated v2 contracts, compatibility, errors, examples and executable assertions. |
| New `MCP_API.md` | Conversation-tool schemas, result limits, scope, transport and examples. |
| New Docker/connection guides | CLI login import steps, Ollama/OpenAI-compatible auth recipes, Compose startup and troubleshooting. |
| ADRs | API versioning, run/roster model, consensus protocol, context visibility/compaction and credential refresh ownership. |
| Docker/release artifacts | Dockerfile, `.dockerignore`, `.yaml` Compose, secret-free environment example, health checks, supported image architectures and Docker Hub README. |

Docker health checks include curl, probe the in-container service at `127.0.0.1`, use a five-second interval, two retries and a timeout no greater than the interval. Model actual Docker health transition behavior accurately. Compose waits for required database/service health where applicable. Provide non-destructive update helpers consistent with repository requirements, preserving named volumes and clearly documenting any interruption. No destructive reset or public image publication is part of this planning task.

Release preparation synchronizes approved v2.0.0 product/version metadata across application builds, dashboard packaging, documentation and distribution artifacts after implementation is ready. Do not invent a prerelease label. Keep protocol version changes independently justified, and record AOT support/applicability after normative review. Build and smoke-test supported Windows, macOS and Linux distributions and relevant Docker architectures. Built, tested and publicly published are separate statuses.

## 15. Delivery sequence and gates

Each phase produces code, meaningful tests, contract changes where applicable and user documentation in the same slice. Checkboxes below reflect recorded acceptance evidence; implementation status and remaining work are tracked in [section 17](#17-implementation-evidence-and-active-execution). Completion requires linked evidence, including commands, commit, environment, results and explicit skips; writing an implementation or passing one fixture is insufficient to close an entire phase.

| Phase | Deliverable | Dependencies | Exit evidence |
| --- | --- | --- | --- |
| P0 | Decisions, requirements reconciliation, compatibility and architecture ADRs; complete surface inventory. | Confirmed product decisions in section 2 and current baseline review. | Accepted semantics, reviewed hash changes and traceable scope/test matrix. |
| P1 | Configuration resources, endpoint auth, encrypted credentials and Docker credential prototype. | P0. | Auth/redaction tests, Codex refresh compatibility evidence and migration round trips on both databases. |
| P2 | Generalized roster, explicit mode, service scheduler and v2 lifecycle contracts. | P0, P1 profile snapshots. | 2/3/4/8/16/32 seat service tests, restart recovery and historical compatibility. |
| P3 | Model-guided questioning, proposal/ratification protocol and persistent interjections. | P2. | Reference four-agent debate, stale-vote race tests and every interjection timing case. |
| P4 | Context manifests, model compaction, evidence tools and CLI bridge. | P1, P3. | Source fidelity/visibility, context overflow, tool parity, summary cancellation and clarification tests. |
| P5 | Complete dashboard, TUI, CLI/client parity and documentation workflows. | Vertical slices from P1 through P4. | Automated journeys plus recorded accessibility/platform review and full source inspection. |
| P6 | Postman collection assertions, dependency simulations, Docker distribution and scale testing. | P1 through P5. | Reproducible CI evidence, no contract drift, safe credential persistence and measured capacity. |
| P7 | Release acceptance and v2.0.0 packaging. | P0 through P6. | Full acceptance record, reviewed limitations, verified documentation and separately authorized publication. |

- [x] P0: reconcile requirements and finalize technical contracts using the confirmed product decisions. Evidence: [requirements review](docs/REQUIREMENTS.md#november-3-2026-v2-reconciliation), refreshed reviewed hashes, [protocol ADR](docs/adr/0006-multi-agent-debate-protocol.md), REST/MCP contracts and the surface inventory in [section 17](#17-implementation-evidence-and-active-execution).
- [x] P1: prove reusable connections and credential lifecycle. Evidence: SQLite/PostgreSQL configuration and encrypted-credential checks, serialized/uncertain refresh fixtures, pinned Codex 0.166.0 parser compatibility, and Linux arm64 container import/injection/rotation/replacement/missing-key recovery. Live provider authentication remains explicitly unverified.
- [x] P2: prove n-agent scheduling, storage and migration. Evidence: shared `MultiAgentChecks`, `CatalogMigrationChecks`, ownership/restart descriptors and both-database passes in section 17; 2/3/4/8/16/32/64-seat fixtures and concurrent 2-through-32-seat benchmarks.
- [x] P3: prove orchestration, interjection and consensus semantics. Evidence: `MultiAgentChecks` (four-agent reference panel, every interjection timing case, T05 context race, T10 private-directive scoping) and `ProtocolChecks` (T02 field-specific admission, the single structured-output repair, and the T05 proposal-edit race) in [checkpoint A](#checkpoint-a-p3-closure-november-4-2026), with the November 4 verification table.
- [x] P4: prove compaction and model-accessible evidence. Evidence: `ContextChecks` (source fidelity, visibility, overflow, failed-rebuild preservation, summary cancellation, blocking clarification, pinned-constraint attribution), `EvidenceToolChecks` on SQLite and PostgreSQL (tool contracts), `McpChecks` (bridge parity) and the browser summary journey; see [checkpoint B](#checkpoint-b-p4-tool-contracts-november-4-2026). Semantic summary quality remains a live-model human review under P7.
- [x] P5: prove parity across every supported interface. Automated parity, layout, structural accessibility, RTL and pseudo-locale evidence is recorded in [checkpoint D](#checkpoint-d-p5-interface-items-november-4-2026). The manual screen-reader pass and native terminal review were removed from scope on November 4, 2026.
- [ ] P6: prove contract, provider simulation, Docker and scale behavior. Contract drift, provider-failure simulation, Docker health and container acceptance evidence is in [checkpoint E](#checkpoint-e-p6-docker-and-provider-simulation-november-4-2026). Open: a hosted CI run. Non-arm64 container verification was removed from scope on November 4, 2026.
- [ ] P7: complete release evidence and prepare v2.0.0 artifacts.

Frontend and Postman work should accompany the backend slices rather than accumulate until the end. P5 and P6 close cross-feature gaps; they are not permission to defer user workflows or tests during P1 through P4. Do not estimate calendar dates until P0/P1 resolve credential, harness and compatibility uncertainty.

## 16. Risks and implementation validation

The hardest failure is a convincing but false account of agreement or prior context. Proposal-bound votes, durable interjection epochs and source-linked summaries address different parts of that problem. They still require UI clarity and quality evaluation. A summary can be structurally valid and semantically wrong; a unanimous panel can still be wrong.

| Risk | Required mitigation / decision |
| --- | --- |
| Prompt growth and cost scale with n and transcript length. | Bounded scheduling, per-purpose budgets, model-aware context preparation, measured benchmarks and visible usage. |
| An orchestrator steers toward its own preference. | Pointed but neutral questions, source references, fair participation and participant-only ratification. |
| CLI tool protocols and login formats change. | Pin/test versions, record capability evidence and fail with explicit compatibility guidance. |
| Shared login refresh races invalidate Codex credentials. | Dedicated login import, one refresh owner, atomic persistence and explicit uncertain-refresh recovery. |
| Query/header credentials appear in diagnostics. | Redact before capture, write-only storage APIs, synthetic canary tests and secret-free exports. |
| Private directives leak through shared memory. | Visibility partitions applied before every summary/search/tool result, with adversarial regression cases. |
| Provider-compatible branding hides missing tools or schemas. | Capability declarations and explicit test completions; no unsupported silent fallback. |
| Roster removal manufactures apparent unanimity. | Preserve removed-seat dissent and membership changes, invalidate votes and display the final denominator. |
| Multiple local tools alter the same work tree. | Sequential turns initially; explicit execution locations and no claim that working directory is a sandbox. |
| Major-version changes strand old clients or live runs. | Compatibility ADR, capability checks, historical reads and explicit legacy-to-v2 conversion. |

The product questions are resolved: every active agent must agree, the moderator can change the roster at safe boundaries, and every interjection offers Interrupt now or Use in next turn. No further moderator answers are needed to complete this plan. The proposed capacity of 16 seats and sequential scheduling should be revisited with workload evidence, without redesigning the collection-based model. Exact provider model IDs, endpoint URLs and credentials are deployment configuration; no private values are needed to review or implement this plan.

## 17. Implementation evidence and active execution

This plan is the sole implementation checklist. The local evidence below records completed checks; execution continues against all requirements and gates above.

### Surface and post-implementation review

Automated layout checks and human-readable screenshot/frame inspection are distinct evidence. Browser reviews cover 390, 768 and 1280 pixels in light/dark themes. Terminal form reviews cover 60, 80 and 120 columns; panel setup additionally covers 4, 16 and 64 selected seats. Screenshots and frame captures are local `.run/` artifacts, with reproducible tests committed alongside the implementation.

| Surface | Functional evidence | Post-implementation UX/styling review |
| --- | --- | --- |
| Web Overview / Conversations | Existing navigation, editing, history paging and replay journeys; route coverage in `multi-agent.spec.js` | Both themes and all three widths. Fixed one-character ID wrapping on mobile; checked readable rows and contained table scrolling. |
| Web Workspace | Four-agent setup, explicit context, durable delivery, unanimous votes, original history, tool activity and recovery journeys | Responsive transcript/composer, proposal expansion and vote labels. Fixed excess page canvas height and mobile heading/actions. Conversation retrieval is labeled separately from local execution tools. |
| Web Participants / Connections | Credential import/no echo, endpoints, harnesses, models, discovery, participant selection and separate orchestrator | All four configuration forms and the discovery dialog in both themes/three widths, masked input, reachable Save/Cancel, modal containment and visible errors. |
| Web Settings | Service limits and settings contract | Light/dark responsive forms; removed obsolete password-login guidance from the open workspace. |
| Web Request history | Redacted credential writes, request inspection, filters and chart journeys | Reviewed loaded chart/table, axes and pointer/keyboard behavior; screenshots wait for chart data rather than capturing only its shell. |
| Web API Explorer | Generated operations and request/response handling | Both themes/three widths, labeled parameters and contained output; discovery is explicitly marked as contacting the endpoint. |
| TUI setup / configuration | Actual wizard/form keyboard tests, order changes, 64-seat selection, explicit orchestrator, secret masking | Narrow/wide and both themes, scrolling to final controls and consistent palette. |
| TUI conversation / recovery | Real service and HTTP provider simulation: four agents, both delivery modes, interrupt, resume, four votes, proposal/context/memory/clarification inspection | Captured and inspected delivery chooser and full proposal reader. Empty summaries now explain when compaction occurs and retain original-history guidance; unavailable paging actions are omitted. Composer wording consistently says Add context. |
| CLI / native client | 57 assertions against an isolated service, including catalog CRUD/import/disable/discovery and four-agent lifecycle | Reviewed help, JSON output, stdin/file secret input, useful errors, noninteractive behavior and typed client parity. |
| REST / Postman | Generated OpenAPI and collection; isolated runnable acceptance workflow with negative validation, idempotency, interruption, discovery and four votes | Reviewed descriptions, redaction, bounded catalog pagination, examples and recovery responses. No workspace publication or cloud credentials required for local assertions. The installed CLI does not implement `postman status`; local artifact generation and collection execution provide the setup evidence. |
| Native MCP / CLI adapter | Real fixture child processes negotiate Claude/Codex MCP, retrieve scoped evidence, reject duplicate/unknown calls and enforce context budget | Reviewed tool names, bounded arguments/results, invocation lifetime and actionable failures. Native Gemini/Mux/generic harnesses use the documented response envelope. |
| Docker / update helpers | Dockerfile, Compose, health checks, installed harness pins, separate persistent data/key/work volumes and optional Ollama profile and non-destructive helpers implemented | Linux arm64 image build and 27 container acceptance assertions passed. Reviewed packaged help, dashboard assets, loopback host/origin errors, key recovery and replacement behavior. Native TUI interaction and full dashboard styling are covered separately above. Other container architectures remain unverified. |
| Native distributions / launchers | Manifest-driven Publisher, six self-contained archive targets, Windows Inno/macOS pkg/Linux deb/rpm/AppImage recipes, checksums and native CI matrix | macOS arm64 app payload and Linux arm64 packages each passed 11 native lifecycle assertions including browser destination. Linux desktop entries validate; deb/rpm installation, replacement and removal retain workspace data. Windows native execution, graphical OS installer review and hosted CI results remain unverified. |

### Verification and reproduction

Local evidence files are ignored build/test artifacts. The corresponding commands and tests are part of the repository.

| Check | Observed result / evidence |
| --- | --- |
| Locked restore and Release solution build | Passed with zero warnings/errors; `.run/v2-restore.log`, `.run/v2-final-build.log`. |
| Shared console suite | 74 descriptors: 72 passed, 2 PostgreSQL checks skipped without its connection variable; `.run/v2-environment-console.json`. PostgreSQL is exercised separately below. |
| Shared xUnit / NUnit adapters | Both adapters passed on net8.0 and net10.0; `.run/v2-final-xunit.log`, `.run/v2-final-nunit.log`. Their aggregate result does not erase provider skips reported by the shared inventory. |
| Runner suite | 25 runner descriptors passed in the full console inventory, including 2/3/4/8/16/32/64-seat panels, last-vote interjection, structured/private summary rejection and lineage, native MCP, model checks, generation settings, auth and migration; `.run/v2-environment-console.json`. |
| PostgreSQL | 2 passed, including the reusable four-seat panel, credential round trip, summary/evidence/clarification checks and ownership/lost-writer behavior; `.run/v2-environment-postgresql.log`. Run `ARGUEBOT_POSTGRES_BIN=<PostgreSQL-bin-directory> node scripts/test-postgresql.mjs` to start and remove an isolated loopback cluster. |
| Terminal | 20 passed, including the real-service four-agent journey, memory lineage, request previews, model checks and layout reviews; `.run/v2-preview-terminal.json` and `.run/tui-v2-review/`. Set `ARGUEBOT_SIMULATION_URL` to include the HTTP panel journey. |
| Web | All 27 browser journeys passed together with the provider simulation enabled in `.run/v2-memory-browser-all.log`; the three multi-agent/layout journeys passed after generation and request-preview additions in `.run/v2-preview-browser.log`. The final help-string correction and expanded model form passed in `.run/v2-preview-layout-final.log`. 25 component tests passed; lint, production build and i18n checks passed. |
| CLI | 57 assertions passed in `.run/v2-environment-cli.log`, using `ARGUEBOT_SIMULATION_URL=... node scripts/test-cli-surfaces.mjs`. |
| Postman | `node scripts/test-v2-postman.mjs` passed 65 saved workflow requests with 135 assertions and zero failed assertions in the latest run; `.run/v2-environment-postman.log` (polling changes the observed assertion count). Provider HTTP responses are simulated; service persistence and orchestration are real. |
| Codex parser compatibility | Pinned `codex-cli 0.166.0` accepts API-key and sanitized ChatGPT documents: `.run/v2-codex-parser.log`. `scripts/test-codex-login.mjs` uses fictional credentials and isolated CLI storage. It does not establish live authentication or generation. |
| Contract generation | `node scripts/generate-api.mjs` produces 80 operations and 53 reachable DTO schemas; `node scripts/generate-v2-workflow.mjs` produces the isolated acceptance collection. |

CI now declares Windows/macOS/Linux runtime jobs, both shared test adapters, browser journeys, PostgreSQL/provider simulation, local Postman assertions, offline pinned Codex compatibility and Docker build/restart smoke checks. Those workflow edits have not been run on hosted CI in this session. Branch protection has not been changed.


### Active implementation: budgets and orchestration policy

Schema 15 adds per-run participant/orchestration/compaction/tool reservations, an elapsed deadline, a per-invocation character ceiling, an optional conservative initial-input estimate ceiling, and separate question/proposal/summary guidance. These settings are frozen at admission, exposed in capabilities and the run contract, editable in web/TUI setup and accepted by CLI/native-client requests. Runtime inspection shows usage and limits. The estimate is initial-input UTF-8 bytes plus framing allowance, not billed provider tokens, and excludes continuations/output. Currency caps remain unavailable without enforceable provider pricing/accounting. Pre-policy runs retain their original deadline behavior.

The service tests prove refusal before external invocation, independent counters, explicit retry without resetting reservations, tool identity idempotency and deadline cancellation. UI, Postman, both-database and final styling checks accompany this slice.


### Execution evidence: capacity, private memory and credentials

Endpoint profiles now expose a 1 through 64 shared generation limit. The service admits queued invocations in order, removes cancelled waiters and releases capacity after completion or failure. The endpoint identity is frozen in run snapshots; the resource capacity is read for each new admission. Capacity changes do not cancel existing invocations. Inline legacy REST profiles share a conservative single slot per exact base URL. The real Ollama/OpenAI simulation checks observed one and two simultaneous invocations under the corresponding limits and cancellation of queued work.

Context compaction maintains separate shared and recipient-private summaries. The orchestrator receives only the selected partition while compacting; ordinary question and proposal invocations cannot retrieve another seat's private summary. Original records remain intact. Source manifests return 100 identities per page with exact totals; REST, model tools, web, TUI, CLI and the native client can retrieve subsequent pages. A 125-source private fixture validates repeated compaction, paging and denial to other seats and the ordinary orchestrator. SQLite runner checks and the PostgreSQL provider suite passed after these changes.

Credential provenance records the exact revision resolved with each encrypted value, before external invocation. Repeated auth bindings share one resolution within an invocation. Master-key rotation installs a protected versioned keyring before transactional re-encryption, retains previous keys for older backups, and leaves authentication revisions unchanged. The native client, REST/API Explorer, web, TUI, CLI and Postman expose safe key status and compare-and-swap rotation. Tests cover stale rotation, restart with mixed old/new ciphertext and rollback of failed database re-encryption. The keyring must be backed up separately from the database.

The policy/capacity/private-memory runner pass recorded 24 successful descriptors in `.run/private-runners.json`; the corresponding PostgreSQL pass recorded two successful provider descriptors in `.run/private-postgresql.log`. Web review passed both multi-agent journeys, including six policy captures and all route/form captures in `.run/private-browser.log`. The post-facto screenshot inspection found visible translation keys in policy help; those keys now have explanatory text. The policy/private-memory Postman workflow passed 93 assertions before the later key-rotation additions.


### Execution evidence: observed capabilities and scale

Explicit model checks now have durable command identities, frozen model/transport revisions, safe completion/tool observations and bounded history. Web, TUI, CLI, native client, REST/API Explorer and Postman distinguish discovery from potentially billable completion checks. Concurrent command retries deduplicate; restart marks unfinished checks uncertain without replay. Supported CLI checks also attempt a bounded executable-version read and show unverified when it is unavailable. Ollama, OpenAI and generic CLI fixtures passed completion/tool checks, a completion without the requested probe, changed-configuration retries and uncertain recovery.

`scripts/benchmark-v2.mjs` starts its own service and home and measures two concurrent conversations at n = 2, 4, 8, 16 and 32, with forty original evidence notes per conversation and a 300 ms HTTP provider fixture. It records exact environment, dirty commit state, admission and first-observed-text latency, interjection latency, database growth, resident memory, context sizes, compaction count, usage and endpoint queue wait. The first run exposed underestimated REST framing; prompt preparation now measures the actual tool request and reserves response continuation space before invoking. An authenticated HTTP regression covers this behavior.

Observed on macOS arm64 with 18 logical CPUs: SQLite completed all five panel sizes with interjection p95 270.5 ms; PostgreSQL completed all five with interjection p95 8.6 ms. The 32-seat pair took 37.4 s and 34.8 s respectively, including simulated provider latency. These are local fixture observations, not live-provider throughput guarantees or portable CI thresholds. Reports are `.run/v2-benchmark-sqlite.json` and `.run/v2-benchmark-postgresql.json`. Reproduce with `ARGUEBOT_SIMULATION_URL=http://127.0.0.1:4491/ollama node scripts/benchmark-v2.mjs`; add `ARGUEBOT_BENCHMARK_POSTGRES_BIN=<PostgreSQL-bin-directory>` for an isolated PostgreSQL cluster.

The updated CLI workflow passed 54 assertions; the Postman workflow passed 104 assertions. The model-check browser journey and all route/form captures passed. Terminal model-check and key-rotation journeys passed at 60, 80 and 120 columns in both themes; the source-order assertion in the attempt inspector was updated to account for the newly displayed endpoint wait field.


### Execution evidence: structured memory and delivery provenance

Schema 21 adds normalized memory sections. New compactions require seven distinct categories and preserve historical unstructured records. Missing/duplicate categories and missing/invented source references reject activation atomically. SQLite and PostgreSQL checks retain the previous valid summary and all originals after each rejection. Source hashes and parent-summary links are exposed through REST, native client, CLI JSON, model retrieval, web and TUI. The browser source/parent journey and terminal provenance reader have light/dark captures at the supported widths.

The expanded Postman journey exposed a delivery-reporting gap when an unpinned interjection was compacted before a participant saw it. Delivery now separates original input from source-linked summaries, instead of labeling summarized input as direct delivery. Pinned originals remain directly selected. Mechanical validation and hash equality do not establish semantic summary quality; live-provider corpus review remains explicitly unverified.


### Execution evidence: generation configuration

Schema 22 stores reusable HTTP output-token, temperature and top-p settings and their immutable participant snapshots, with a separate service output-capture byte limit for all transports. The web/TUI model editors and CLI/native/API contracts expose them. The runner applies typed PolyPrompt 3.4 completion options for every continuation; absent token settings retain its 4096-token default, while absent sampling settings retain provider defaults. CLI-specific parameters remain literal harness arguments and incompatible HTTP overrides are rejected. The authenticated Ollama/OpenAI fixture verifies the exact wire options, and storage checks cover revision changes, fractional precision and clearing overrides.


The curated [quality corpus](assets/fixtures/debate-quality.json) and [review procedure](docs/DEBATE_QUALITY.md) cover contradictory measurements, late corrections, changed assumptions and persistent dissent with an untrusted quoted consensus claim. Expected observations and unacceptable claims are explicit. These are review inputs; no unrun live-model case is labeled passed.


Saved endpoint previews now expose normalized destinations and redacted header/query names without resolving credentials or sending provider traffic. REST/API Explorer, CLI/native client, web and TUI share this read operation. Endpoint-preview and expanded model-form screenshots/frames cover both themes and all configured widths. The visual pass caught missing generation-help translations; these were supplied, and generic fields no longer render untranslated optional hint keys.


The final scale rerun completed all ten paired workloads on schema 22; each report includes a source-tree SHA-256 in addition to the commit and environment. Model `get_summary` retrieval now pages the rendered memory text separately from its source-hash manifest, avoiding repeated full structured content in tool responses. Tests reconstruct the exact summary from bounded excerpts and reject nonadvancing Unicode excerpts and invalid source-page cursors.


### Execution evidence: container acceptance and harness environments

The Linux arm64 application image built and passed 27 container assertions in `.run/v2-environment-container.log`. Tests create a unique Compose project, use a loopback-published port and remove only their own volumes. They exercise packaged service/dashboard/CLI/TUI entry points and pinned harness versions, a four-seat unanimous debate, all three credential kinds, actual Claude environment/Codex private-file injection through fictional harnesses, key-file permissions, master-key rotation, container replacement, missing-key refusal and recovery, and absence of leaked credentials in metadata/logs. The real pinned Codex binary also accepts sanitized offline login documents. This establishes container lifecycle behavior, not live provider authentication.

Docker Desktop's existing virtual disk was full. Verification used a separate disposable Lima 2.2.1 VZ guest and Docker 29.9.3 daemon on Linux arm64. Existing Docker Desktop images, volumes and containers were untouched. The clean build caught a missing dashboard icon copy; the running service exposed a Host check that used listener metadata instead of the request authority. Both are fixed, with matching/mismatched browser-origin and hostile-Host regression assertions. CI uses the same acceptance script, but its hosted Linux x64 run has not been executed here.

Schema 23 adds normalized inherited-environment selections and frozen participant snapshots. Reusable harnesses keep standard platform/path/locale/proxy/TLS settings plus explicitly named variables. Imported credential injection wins, unrelated service variables are omitted, and selected values are redacted from saved output. Historical inline profiles keep their prior behavior. Web/TUI editors, CLI/native contracts, API Explorer and Postman expose names only. Both database providers and real child-process checks passed, including invalid/duplicate names, snapshot immutability and echoed-value filtering. The full console suite passed 72 descriptors with the two PostgreSQL descriptors separately passing. CLI passed 57 assertions; the updated 65-request Postman workflow passed 135 assertions.

All three multi-agent browser journeys passed after the new harness field; its dedicated visual pass passed at 390/768/1280 pixels in light/dark themes. The 20 terminal descriptors passed, including dedicated inherited-environment frames at 60/80/120 columns in both themes. Current browser evidence is `.run/v2-environment-browser.log` and `.run/v2-environment-layout.log`; terminal evidence is in `.run/v2-environment-console.json` and `.run/tui-v2-review/`.

### Execution evidence: distribution packaging

`publisher.json` and `ArgueBot.Publisher` now drive six self-contained archive targets and native Inno/pkg/deb/rpm/AppImage formats. The wrappers and native CI matrix share this path. Packaging selects the exact reviewed SDK, enforces per-runtime dependency locks, checksum-verifies downloaded tooling and produces SHA256SUMS. Native rebuilds stage their output before replacing existing artifacts. Every payload includes the service, CLI, TUI, dashboard, dependency inventory and unsigned-software notice. The macOS app has an icon and free ad-hoc signature; Windows and Linux provide terminal/browser launchers. AppImage defaults to the TUI and offers `--web`.

All six archives built. The macOS arm64 pkg and Linux arm64 deb/rpm/AppImage also built, producing ten local artifacts under `installers/0.1.0/`. This retains the current approved build version while v2.0.0 release acceptance is open. Artifacts are not published. The final solution build passed with zero warnings/errors in `.run/v2-packaging-final-build.log`; the new shared Packaging descriptor passed in `.run/v2-packaging-tests.json`.

The expanded macOS app passed signature/plist checks and eleven native assertions in `.run/v2-package-app-final-native.log`. The Linux deb and rpm each passed eleven packaged lifecycle assertions plus four installer assertions covering desktop entries, same-version replacement, retained conversations and unchanged database bytes after removal in `.run/v2-package-linux-installers.log`. AppImage executed with extraction mode and its payload passed eleven assertions in `.run/v2-package-appimage-native.log`. Tests used isolated workspaces with spaces/Unicode paths and loopback ports; browser commands were intercepted to verify the selected URL without opening a real browser. The disposable Linux packaging container was removed after artifact collection.

This evidence does not establish a macOS system-wide installer transaction, a prior-version package upgrade, Fedora dependency resolution (the Ubuntu RPM fixture uses `--nodeps`), Windows native execution, graphical OS installer styling or hosted CI completion. Package-manager manifests/publication and those native acceptance gates keep P7 open. The build-only workflow neither publishes a release nor changes repository permissions. Reproduction and platform distinctions are documented in [packaging/README.md](packaging/README.md).

### Review findings (November 3, 2026)

An independent review of the uncommitted tree reproduced the recorded evidence. Results: a clean Release build; 73 console descriptors passed with the CLI fixture and provider simulation; the 2 PostgreSQL descriptors passed; xUnit/NUnit passed on net8.0/net10.0; dashboard lint, 25 component tests, build and i18n passed; the CLI passed 57 assertions; Postman passed 135; generated API artifacts showed no drift. The review also found defects that the tests did not cover.

Fixed in this pass, with regression checks in `MultiAgentChecks`:

- Extending a protocol 2 debate after `max_rounds` re-queued it with the speaker index past the roster, so it paused with `invalid_state` and could never resume. Extension now opens a new question round, and earlier dissent stays recorded. The new check fails without the fix.
- Interjections read the run outside their write transaction. A concurrent service checkpoint could roll back the moderator text with `revision_conflict`. Creation now rereads and retries.
- Implicitly targeted interjections stored an empty run ID while the receipt reported the real one.
- A runner returning normally after cancellation could record late output as a completed answer or vote. The engine now treats it as interrupted.
- The summary-source existence check could never fire, and it ran after summary rows were written. Sources are now resolved before any write.
- `rebuild_summary` deactivated recipient-private summaries that it never rebuilds. It now deactivates only the shared partition.
- With rotated ciphertext but a missing `credential.keys.json`, the service started on the legacy key alone and failed later per credential. Startup now refuses with a restore instruction. The Docker guide names the keyring.
- The MCP fixture called `get_turn` with `message_id` instead of the contract's `id`.

Open defects and gaps (updated after the follow-up pass below):

- Frequent public `next_turn` context resets round progress each time and can delay ratification indefinitely. This is intended (every applicable interjection invalidates agreement); a future batching option may be worth evaluating.
- Still untested: over-limit and duplicate-name rosters at run creation (T02).
- Spec deviations: there is no single structured-output repair attempt; duplicate seat names are rejected only on add; rejections are not field-specific; there is no proposal-edit operation.
- Credentials:
  - Codex refresh writes `refresh_uncertain` before the request is sent, so DNS, 5xx and pre-send cancellation force reauthentication. That status is undocumented in the API and UIs.
  - Rotation never retires keys.
  - One unreadable credential aborts re-encryption after the new key is active.
  - Allowlisted harness environment values are redacted from model output as secrets, which also suppresses CLI failure diagnostics.
- Tools:
  - `list_turns` lacks the seat/phase filter and a next cursor.
  - `search_context` returns leading text rather than match context, does not escape `LIKE` wildcards, and differs in case sensitivity between SQLite and PostgreSQL.
  - `get_supporting_details` and `get_debate_state` omit claim links, constraints and positions.
  - `get_turn` reads public turns from other runs in the conversation.
  - MCP call IDs do not deduplicate harness retries of `request_clarification`.
- Docker: health checks lack `start_period`; there is no secret-free environment example; the Ollama health check does not use curl.
- Trust model: when local execution is enabled, REST tools and harnesses run with service permissions and can read the protected secrets directory. This matches the documented open-workspace model, but Docker and operations guidance should state it explicitly.

### P5 and follow-up evidence (November 3, 2026)

Fixes with regression checks:

- **Failed rebuild (T13):** schema 24 adds `debate_state.rebuild_from_originals`, and schema 25 adds `debate_agreement_revisions`. A moderator rebuild no longer deactivates any summary up front. Its compaction ignores the active shared summary and works from original evidence, and only a validated replacement deactivates the prior summary. A failed rebuild pauses with `summary_validation_failed`, keeps the previous summary active and stays retryable. The run contract exposes `rebuildFromOriginals`. `ContextChecks` covers a failed rebuild followed by a successful one.
- **Blocking clarification (T16):** a turn that leaves a blocking clarification open no longer advances the speaker, records a vote or creates a proposal. The run pauses until the moderator answers or skips.
- **New `MultiAgentChecks` cases:**
  - abstain and needs_information votes;
  - a prose consensus phrase in a ratification;
  - `next_turn` context committed while the final vote is generating (T05): the stale vote is not recorded, and fresh unanimity is required at the new epoch;
  - interjection after finalization (`post_run`, no new attempts);
  - replacing a seat;
  - removing a dissenting seat with its dissent preserved;
  - the two-seat minimum;
  - a one-seat start.
- **Private `list_turns` (T10):** `ContextChecks` now asserts private-directive exclusion from `list_turns`.
- **Negative checks:** each new test was confirmed to fail with its fix reverted.

UX and usability pass, reviewed from fresh screenshots and frames:

- **Dashboard (web):**
  - Protocol 2 context and clarification answers now require an explicit Interrupt now or Use in next turn choice for every submission. The choice is kept with an unsent draft and reset after sending; previously the selector defaulted to, and persisted, Use in next turn.
  - Completed debates show protocol-aware outcomes ("Unanimous: every active seat agreed to the same proposal") instead of the legacy declaration caveat. The run bar has a Proposals and positions action, and finished runs open their proposals.
  - Orchestrator questions and phases are no longer labeled "Follow-up question".
  - Home shows a Needs your attention count and a per-conversation debate status (Paused: needs you, N agents debating, Unanimous). These load separately from the page.
  - The Participants metric was renamed, table IDs no longer wrap mid-identifier, and narrow workspaces hide message IDs to keep the transcript readable.
  - 34 connection-form help texts that only repeated their labels now explain the field. The request history column is labeled Path. Leftover account wording is removed from outcomes.
- **Terminal (TUI):**
  - Delivery choices explain their effect.
  - The proposal reader numbers proposals and shows the vote tally.
  - The status bar pluralizes conversation counts.
  - The welcome text reflects panels and orchestrators.
  - Debate review lists the question, panel and orchestrator before budgets.
  - Summary titles no longer truncate at 60 columns.
  - Credential kinds use readable names.
- **CLI:** help is grouped into Service, Conversations, Debates, Memory and Configuration, with aligned descriptions, no duplicated `interject` entry, and documented JSON fields for pin, recipient and clarification skip. The service banner no longer says "replacement".

Verification on the final tree, in an isolated copy with loopback fixtures:

| Check | Result |
| --- | --- |
| Release build | 0 warnings, 0 errors |
| Console suite (CLI fixture + provider simulation) | 73 passed; the 2 PostgreSQL descriptors were skipped there and passed in the separate PostgreSQL run |
| xUnit / NUnit | Passed on net8.0 and net10.0 |
| Terminal suite | 20 passed |
| Dashboard | Lint, i18n (679 strings), 25 component tests and build passed; 26 browser journeys passed |
| CLI | 57 assertions passed |
| Postman workflow | 137 assertions passed |
| Generated API artifacts | No drift |

Two browser journeys were timing-sensitive under full-suite load. The attempt-inspector journey now waits for the final outcome. The clipboard proxy tolerates requests abandoned during navigation. Both pass repeatedly in isolation.

Remaining P5 work:

- The DebatePanel still renders after the transcript, so long debates rely on the run-bar action to reach it.
- Tool start/finish activity appears as separate transcript cards.
- The API Explorer gives sparse parameter guidance.
- No component test covers the clarification answer dialog.
- Screen-reader, RTL and pseudo-locale review, native Windows/Linux terminal review, and hosted CI remain unverified.

### Private-directive scoping (T10, November 3, 2026)

**Previous behavior.** Every interjection advanced the shared context epoch, including a recipient-only directive. That epoch appears in every seat's prompt and in `get_debate_state`, and the interjection also reset round progress. Other seats could therefore infer that a hidden directive existed.

**New behavior:**

- A directive with a recipient does not change the shared epoch, phase, proposal or speaker. Its receipt reports the current epoch.
- Interrupt now cancels only the recipient's own active response or vote. Otherwise the directive is queued, and its receipt says `queued`.
- The directive is delivered at the recipient's next invocation.
- **Finalization guard:** suppose all seats have voted and the run would finish (unanimity or round limit), but an active seat still has an undelivered directive. Only that seat votes again on the same proposal. Its earlier vote moves to the new `debate_agreement_revisions` table (schema 25) and remains readable as `superseded`; only the newest vote counts.
- Vote scheduling now picks the next seat without a current vote.
- The web panel and TUI proposal reader show a current-vote tally and mark earlier votes.

**Limit.** The recipient's changed behavior, and its extra vote before finalization, can still reveal that it was told something. Text, sources, shared counters and other seats' schedules do not.

**Evidence.**

- A new `MultiAgentChecks` scenario sends an `interrupt_now` directive to a seat that already voted while another seat's final vote is generating. It asserts:
  - the receipt keeps epoch 0 with status `queued`;
  - no orchestrator or other-seat prompt contains the directive or a changed epoch;
  - the recipient receives it and votes again;
  - one proposal ends unanimous with the earlier vote superseded;
  - delivery records list only the recipient.
- Disabling either the scoping or the finalization guard makes the scenario fail.
- The persistent browser fixture home also exercised an upgrade from the intermediate schema 24 to 25.

**Final verification:**

| Check | Result |
| --- | --- |
| Release build | 0 warnings |
| Console suite | 73 passed, plus the 2 PostgreSQL descriptors in their own run |
| xUnit / NUnit | Passed on net8.0 and net10.0 |
| Dashboard | Lint, i18n (682 strings) and 25 component tests passed; 26 browser journeys passed |
| CLI | 57 assertions passed |
| Postman workflow | 134 assertions passed (the count varies with polling) |
| Generated API artifacts | No drift |

The clipboard journey's cleanup no longer waits on proxied requests that never finish.

### Checkpoint A: P3 closure (November 4, 2026)

**Field-specific admission errors (T02).**

- `DomainException` and `ApiError` carry a camel-case `field`. Run admission now rejects each case separately, before any model call:
  - `too_few_participants`, `participant_limit`, `duplicate_participant` and `duplicate_seat_name` (case-insensitive) on `participantIds`;
  - `profile_not_found` and `disabled_profile` on `participantIds` or `orchestratorId`;
  - `missing_orchestrator`;
  - `invalid_rounds`, `invalid_countdown` and `invalid_key`;
  - `invalid_policy` naming the exact `policy.*` field.
- Duplicate seat names were previously rejected only when adding a seat.
- How each surface shows it:
  - the web dialog marks the field inline, blocks duplicate names before submit, and refreshes and drops a deleted or disabled selection;
  - the TUI wizard validates duplicate names and prefixes service rejections with the step to fix;
  - the CLI prints `code (field): message`;
  - the native client keeps `field`.

**Structured-output repair (one bounded attempt).**

- A question, proposal or vote that is not the required JSON, or that names another proposal, stays as a failed attempt.
- The same step then runs once more with a correction request. It is marked `repair` in `debate_attempts` (schema 26) and shown as **Correction request** in web and TUI attempt details.
- If the repair also fails, the run pauses with `invalid_orchestration_output`. A moderator resume after that is an ordinary attempt.
- The repair counts toward the run budgets.

**Moderator proposal edits (T05 edit half).**

- The `edit_proposal` run action requires the reviewed run `revision`. It is available in web (**Edit proposal**), TUI run commands, CLI `debate edit-proposal` and the API.
- In one transaction it inserts a new proposal, deactivates the old one and restarts voting.
- Proposals expose `superseded`, `moderatorEdited` and `supersedesId`.
- A vote that was generating on the old proposal is ignored: it is not counted and is not treated as invalid output.
- An edit after finalization fails with `no_open_proposal`, so the first transaction to commit decides the result.

**Evidence.**

- A new shared `ProtocolChecks` descriptor (`Runners/DebateProtocol`) covers:
  - twelve admission rejections with code, field, readable message, no model call and no run row, and a single-seat follow-up afterwards;
  - a repaired vote reaching consensus with exactly one repair;
  - a failed repair pausing, then a normal retry succeeding;
  - a repaired orchestrator proposal;
  - an edit racing the final vote: the stale vote is not counted, there is no repair or pause, the revision is ratified by all four seats, and the edit is refused after finalization;
  - missing-revision, stale-revision and blank-text rejections.
- Removing the stale-vote guard makes the edit scenario fail (the stale vote triggered a spurious repair).
- A new browser journey shows the duplicate-name block, the deleted-selection field error, the proposal editor, and the badges on the revision and the replaced proposal at 390 and 1280 px in both themes.
- The CLI script asserts `too_few_participants (participantIds)`.
- The TUI start-error frame now shows a field-specific `Speakers:` rejection, and duplicate names fail terminal validation.

**UX pass findings fixed.**

- After a selected profile was deleted, the start dialog told the user to refresh a list they could not refresh. It now refreshes and removes the stale selection itself.
- A replaced proposal showed "0 of 0 current votes agree"; it now explains that it was replaced and how many earlier votes were kept.

**Verification:**

| Check | Result |
| --- | --- |
| Release build | 0 warnings |
| Console suite | 74 passed, 2 PostgreSQL descriptors skipped; PostgreSQL run passed 2 |
| Terminal suite | 20 passed |
| Dashboard | Lint, i18n (700 strings) and 25 component tests passed; 27 browser journeys passed |
| CLI | 58 assertions passed |
| Postman workflow | 134 assertions passed |
| Generated API artifacts | Regenerated |

### Checkpoint B: P4 tool contracts (November 4, 2026)

**Tool changes** (documented in `MCP_API.md`):

- **`list_turns`** accepts `seatId` and `phase`; `phase` can be a debate phase, or `moderator` for moderator context. It returns `items` with seat, phase, size and truncation, plus a `nextAfter` cursor that is null on the last page.
- **`search_context`** requires a query. It matches literally and case-insensitively in the service, using ordinal Unicode folding. Snippets are centered on the match and include `matchOffset`. A call scans at most 2,000 turns or 8 MiB and returns `partial` with `nextAfter`.
  - The first version folded case in SQL. The PostgreSQL run showed that `lower()` follows database collation: a C-locale database did not fold "É". Matching moved into the service so both databases behave the same.
- **`get_supporting_details`** accepts up to 20 `ids`. For each it returns attribution, a bounded excerpt and, for summaries, `linkedSourceIds` of visible originals. Invisible or missing IDs come back as `gaps`, and so do linked sources the seat cannot see. A query with no match returns a gap.
- **`get_debate_state`** adds round, round limit, the pinned constraints visible to the seat and `earlierPositions`. It deliberately omits votes on the open proposal so each seat evaluates it independently.
- **`request_clarification`** returns the existing open request when the same seat repeats the same question during one attempt, which covers a harness retrying with a new MCP call ID.
- **Pinned constraints:** compaction prompts list the pinned constraint IDs they supply. Activation rejects a structured summary whose constraints section does not name each one; the prior memory stays active.
- **`get_turn` scope:** not changed, by decision. Tools see exactly the prompt-context scope: completed public turns of this conversation, including earlier runs, and turns addressed to the seat, up to the source boundary. Earlier public runs are already part of every prompt, so limiting `get_turn` to the current run would hide context the model has already received.

**Evidence.**

- **New `EvidenceToolChecks`**, run on SQLite (`Runners/EvidenceTools`) and PostgreSQL (`Providers/PostgresqlEvidenceTools`). It covers:
  - cursor paging that visits every visible turn exactly once;
  - seat, phase and moderator filters, and rejection of an unknown seat;
  - literal `%`/`_` matching with a decoy, a centered snippet at the exact offset, and non-ASCII case folding;
  - private-context exclusion and blank-query rejection;
  - gaps for private and missing IDs without their content, and a no-match gap;
  - constraints, earlier positions excluding the open proposal, roster and round.
- **`ContextChecks` additions:**
  - summary `linkedSourceIds` stay within the summary's sources and exclude private turns;
  - a harness retry with a new call ID returns the same clarification;
  - an unattributed pinned constraint pauses with `summary_validation_failed` and activates nothing; after correction, the resumed summary cites the source ID.
- **Negative checks:** removing the pinned-attribution guard or the clarification deduplication makes these checks fail.
- **Browser:** the summary journey asserts that the constraints section cites the pinned source ID, and that the citation opens the original.

**UX pass findings fixed.**

- Summary citations of source IDs are now buttons that open the original.
- Moderator-written turns showed the raw speaker `user` in the web transcript, full-message dialog and source dialog. They now show "You", matching the TUI.
- The source dialog's Close button now uses the standard dialog footer.
- The summary-validation explanation now lists the pinned-constraint cause.

**Remaining for P4.** Semantic summary quality is a live-model human review, tracked under P7.

### Checkpoint C: credential follow-ups (November 4, 2026)

**Changes:**

- **Codex refresh.** The login is still marked `refresh_uncertain` before the token request, but the mark is released when the request provably did not reach the login service (name resolution, connection, proxy or TLS failure) or was answered 429 or 5xx. The run then pauses with `refresh_unavailable` and the login stays usable. A 4xx or unparseable success keeps `refresh_uncertain`, so a consumed refresh token is never replayed. `refresh_uncertain` is documented in `CredentialMetadata.status` and shown as **Sign-in needed**, with an explanation, in web and TUI credential lists; credential kinds are labeled too.
- **Rotation pre-check.** Rotation decrypts every stored credential before writing a new key. An unreadable enabled credential refuses rotation with `credential_unreadable`, naming it, and changes no key. An unreadable disabled credential is left unchanged and reported as `skippedCredentials`.
- **Key retirement.** `POST /v2.0/api/configuration/credential-keys/retire` (web **Retire earlier keys** with an explicit warning step, TUI keys menu, CLI `credential-keys retire`, native client) removes earlier keys once no credential uses one. It is refused with `keys_in_use` or `key_changed` otherwise.
- **Environment redaction.** Only inherited values whose names look like credentials (KEY, TOKEN, SECRET, PASSWORD, PASSWD, CREDENTIAL, AUTH, COOKIE, SESSION, PRIVATE) are redacted. Ordinary configuration such as a region or a flag no longer corrupts output or suppresses CLI failure diagnostics.
- **Summary repair, found during this checkpoint.** A summary that fails validation now gets the same single correction request as other structured output. The Postman run exposed this: a summarizer that forgot to cite a pinned constraint paused the debate outright.

**Evidence:**

- `CatalogChecks`:
  - connection refusal, 503 and 429 each leave the login ready with one release revision, and a retry then refreshes normally;
  - a rejected refresh keeps `refresh_uncertain`;
  - a corrupted enabled credential refuses rotation and leaves the key active;
  - once disabled, the credential is skipped;
  - retirement is refused while that credential uses an earlier key, and a stale key ID is refused;
  - after replacement, retirement removes both earlier keys durably and every credential still resolves after reopening.
- `HarnessEnvironmentChecks`: a real fixture process echoes a non-secret inherited value verbatim, and a `*_ACCESS_TOKEN` value comes back redacted.
- `ContextChecks`: an unattributed pinned constraint produces exactly one compaction repair before pausing.
- The CLI script asserts retirement; the four-agent browser journey covers the retirement warning and completion at 390 and 1280 px in both themes.

**UX pass findings fixed:**

- The key dialog's retirement warning first used the neutral notice style; it now uses the danger accent.
- Credential rows showed raw `token · refresh_uncertain` values; they now show readable kind and status labels, with attention styling.

**Verification:**

| Check | Result |
| --- | --- |
| Runners suite (with the CLI fixture) | 17 passed |
| CLI | 59 assertions passed |
| Postman workflow | 135 assertions passed |
| Four-agent browser journey | Passed |

### Checkpoint D: P5 interface items (November 4, 2026)

**Changes:**

- **Debate panel placement.** Proposals, votes, roster, delivery and memory now sit in a scrollable sidebar beside the transcript when the window is at least 1200 px wide. Below that width the open panel is a bounded region above the transcript; under 768 px it replaces the transcript until hidden. The run bar's Show/Hide control replaces the old scroll-to-bottom button, and the choice persists in the browser.
- **Tool cards.** `MessagePresentation.MergeToolActivity` (TUI) and `mergeToolActivity` (web) hide a tool's `started` record when its finished record for the same attempt and call is in view. Storage still keeps both. Unfinished calls stay visible, and identical call IDs from different attempts are never merged.
- **API Explorer guidance.** The explorer now:
  - groups operations by tag and shows each operation's description;
  - gives every parameter a specific description (generated `queryHelp`/`pathHelp` replace the generic "Backend filter" text) with type, range and default;
  - uses number and boolean inputs where they fit;
  - lists the request-body fields with required markers, enums, ranges and defaults;
  - validates JSON inline and blocks Postman `{{placeholders}}`;
  - names missing required parameters and offers Reset to example.
- **Clarification dialog.** New component tests: text and a delivery choice are required; an answer sends the pinned interjection with the clarification ID and an `:answer` key; skip records the question with a `:skip` key; on a conflict the dialog stays open.
- **Accessibility, RTL and pseudo-locale.** A new `accessibility.spec.js` runs an in-page structural audit on every route and on a live debate workspace. It runs in en, ar-XB (RTL) and en-XA (expanded) at 390 and 1280 px, and checks:
  - accessible names;
  - duplicate IDs;
  - one `main` landmark and one visible h1;
  - image alt text;
  - raw translation keys;
  - page and region overflow;
  - document direction.

**UX pass findings fixed:**

- RTL clipped the sidebar text because its padding used physical sides. Five remaining physical left/right rules now use logical properties.
- Two identical copy icons sat in each message header; the turn copy is now a labeled, compact text action.
- On phones, the stacked panel left only a sliver of transcript; it now toggles between transcript and panel.
- The explorer's field names in the reference table were too small; they are now 12 px.

**Not done here:**

- A manual screen-reader pass (VoiceOver, NVDA).
- Native Windows and Linux terminal review.

The structural audit is automated evidence. It does not replace those manual passes.

**Verification:**

| Check | Result |
| --- | --- |
| Console suite (all descriptors) | 76 passed; 3 PostgreSQL descriptors skipped (run separately) |
| Component tests | 29 passed |
| Browser journeys | 28 passed |
| Dashboard | Lint and i18n (737 strings) passed |

### Checkpoint E: P6 Docker and provider simulation (November 4, 2026)

**Changes:**

- **Provider failure classification.** Provider refusals are now:
  - `provider_unauthorized` for 401 and 403;
  - `provider_rate_limited` for 429, with the provider's `Retry-After` seconds in the message, captured by `EndpointAuthHandler`;
  - `provider_unavailable` for 5xx;
  - `provider_error` for other statuses.

  Transport failures are `provider_disconnected` (a midstream drop, with partial text kept) and `provider_unreachable` (connection or DNS failure). Each code has recovery guidance in attempt details and a web outcome label. Before this, every provider refusal was the generic `provider_error`.
- **Simulation scenarios.** The provider simulation adds `forbidden` (403), `server-error` (503) and `disconnect` (a partial NDJSON stream, then a socket reset). These sit alongside the existing `rate-limit` (429 with `Retry-After`), `multi-agent-auth` (401 on a binding mismatch), `broken-stream`, `deadline-stream`, malformed votes and summaries, and tool continuations. Pinned-constraint attribution was added to the simulated summaries.
- **Invalid refresh response.** Covered by a refresh-handler fixture: an unparseable 200 leaves the login `refresh_uncertain` and asks for reauthentication.
- **Docker health.**
  - **Health timing.** The service and Ollama health checks gain `start_period: 30s` and `start_interval: 2s`, with a 10-second interval and three retries; the Dockerfile `HEALTHCHECK` matches.
  - **Ollama check.** It deliberately keeps `ollama list`: the pinned image has no curl, and `ollama list` calls the HTTP API.
  - **Configuration.** The published port comes from `ARGUEBOT_PORT`. A secret-free `docker/.env.example` (tracked through a `.gitignore` exception) documents provider settings, and Compose loads an optional untracked `docker/.env`.
  - **Trust model.** The Docker guide now states it: when local execution is enabled, agents can read `/data/secrets`.

**Evidence:**

- The new `Runners/ProviderFailures` descriptor runs against the simulation through the real PolyPrompt client. It asserts each code, the guidance text including the one-second retry interval, a nonempty explanation, and partial text kept on disconnect.
- `OllamaRateLimit` now expects `provider_rate_limited`.
- `CatalogChecks` covers the invalid refresh response.
- The container acceptance test, on a rebuilt Linux arm64 image (Docker 29.8.2), now asserts that Compose reports `healthy` after startup and that it got there within the start period. It passed 29 assertions.
- `docker compose config` validated the new file.

**Not done here:**

- A hosted CI run, which needs a push.
- Container architectures other than Linux arm64.

**Verification on the final tree:**

| Check | Result |
| --- | --- |
| Release build | 0 warnings |
| Runners suite | 29 of 29 passed, with the simulation and CLI fixture |
| xUnit / NUnit | Passed on net8.0 and net10.0 |
| PostgreSQL suite | 3 of 3 passed |
| CLI | 59 assertions passed |
| Postman workflow | 138 assertions passed |
| Browser journeys | 28 passed |
| Dashboard | Lint, 29 component tests and i18n (745 strings) passed |
| Container acceptance | 29 assertions passed |
| Generated API artifacts | Regenerated with no drift on a second run |

### Remaining work by gate (updated November 4, 2026)

**P3:** closed by checkpoint A.

**P4:** closed by checkpoint B. The live-model review of semantic summary quality is tracked under P7.

**Credentials** (P1 follow-ups): closed by checkpoint C.

**Scope decisions (November 4, 2026, by the product owner):** the manual screen-reader pass, native Windows and Linux terminal review, non-arm64 container verification, Windows packaging execution, the prior-version upgrade test, Fedora dependency resolution and the live four-provider journey are removed from scope. A live debate without a web search service for Mux and API-endpoint models would not be informative for most topics. The version change to 2.0.0 and publication are authorized. The feature/v2.0.0 branch was merged into main and deleted on November 4, 2026.

**P5:** closed by checkpoint D.

**P6:** checkpoint E closed the Docker health and environment items and the simulation scenarios. Open:

- A hosted CI run of the declared workflows on main.

**P7:**

- The product owner's acceptance testing and human review of debate and summary quality, including semantic summary quality (from P4).
- Version set to 2.0.0 (done November 4, 2026). Publication follows acceptance.

**Acceptance feedback (November 4, 2026):** stored credentials were hard to find and had no instructions. Fixed by splitting the web Participants page into Agents, Models, CLI harnesses, HTTP endpoints and Credentials tabs with tables, per-kind credential instructions, matching-kind credential choices on harnesses and the same instructions in the TUI. Evidence: `multi-agent.spec.js` tab, credential guide and harness credential assertions. Later acceptance fixes the same day: copyable commands with macOS, Linux and Windows PowerShell variants, bearer/header/query HTTP credential types (schema 27, `Runners/TypedCredentials`), readable primary-button hover colors (`hover-contrast.spec.js`), and a viewport-safe Actions menu on every table with conversation renaming (`action-menu.spec.js`, `ActionMenu.test.js`).
