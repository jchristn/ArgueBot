# Multi-agent debates for v2.0.0

ArgueBot implements protocol 2 across the web dashboard, TUI, CLI and native client. The original implementation plan and its acceptance evidence are archived in [../archive/MULTIPLE_AGENTS_PLAN.md](../archive/MULTIPLE_AGENTS_PLAN.md). Existing protocol 1 conversations remain readable and retain their original completion rules.

## Configure a panel

The dashboard's Participants page has a tab for each resource: **Agents**, **Models**, **CLI harnesses**, **HTTP endpoints** and **Secrets**. The TUI uses **Connections, models and secrets** in its Agents menu. Configure resources in this order:

1. Import a secret if needed. HTTP tokens, Claude OAuth tokens and complete Codex `auth.json` documents have separate kinds, and the add dialog shows how to obtain each: `claude setup-token` for Claude Code, `mkdir -p ~/.codex-arguebot && CODEX_HOME=~/.codex-arguebot codex login` for a dedicated Codex login (Codex requires the folder to exist; the dialog also shows Linux and Windows PowerShell versions), or an API key from the provider console. Values are encrypted and never appear in lists, request history, logs or model context. The moderator can deliberately show one with **Reveal value** in the secret's Actions menu (after a confirmation), `configuration credentials reveal ID` in the CLI, **Reveal value** in the TUI, or `POST /v2.0/api/configuration/credentials/{id}/reveal`. Only ready secrets can be revealed. Use a safe display name; it appears in configuration lists. A harness without a stored secret uses the login of the user running the service, which does not exist in Docker. Gemini CLI, Mux and generic harnesses take keys through inherited environment names instead.
2. Define an HTTP endpoint or a CLI harness. Ollama uses its native chat protocol; OpenAI-compatible endpoints use chat completions. Harnesses specify a direct executable and one literal argument per row.
3. Define a model bound to exactly one endpoint or harness. Enter its provider model ID, context character budget and tool support. These are declarations, not automatic provider capability detection.
4. Start the debate. Each seat, and the orchestrator, can be a **model** or a **CLI harness** directly; both carry their sign-in (the harness's secret, or the endpoint's authentication bindings), so they work in Docker where there is no local login. **Agents** are optional named seats with role instructions; point them at a model. An agent configured directly as a CLI program has no stored secret and uses the service machine's own login, so it fails with "Not logged in" in a container. The debate setup lists models and harnesses first and flags such agents.

For the requested panel, configure Claude Code, Codex and Gemini CLI harnesses, then a DeepSeek model served by Ollama or an OpenAI-compatible endpoint. The model names and executable paths must exist on the service machine. The service owns every model call; browsers and terminals never call providers directly.

HTTP secrets come in three types: **Bearer token** (sent as `Authorization: Bearer`), **Header key and value** and **Query string key and value**. Header and query secrets store their parameter name, which cannot change after creation; an endpoint binding that uses a typed secret takes its type and name from the secret. The older untyped HTTP token still works, with its placement set on the endpoint binding. An endpoint can combine bearer authentication, a named API-key header, custom header pairs and query-string secrets. Secret values refer to imported secrets or service environment variables. Static values are for nonsecret routing headers only. Remote endpoints carrying secrets require HTTPS. Redirects cannot forward secrets to another destination. Base URLs cannot contain secrets or query strings.

Reusable harnesses inherit standard process paths, home-directory locations, locale, proxy and TLS certificate settings. Use **Inherited environment names** in the web or TUI harness editor to add service-host variable names such as `GEMINI_API_KEY`. The API/CLI field is `environmentVariables`, an array of names; never put values in it. Names are frozen with each admitted participant. Values are read when its process starts, and imported secret injection takes precedence. Unlisted application and provider variables are omitted. Historical inline profiles retain their previous inheritance behavior until replaced with a reusable harness. Values of inherited names that look like secrets (containing KEY, TOKEN, SECRET, PASSWORD, PASSWD, CREDENTIAL, AUTH, COOKIE, SESSION or PRIVATE) are redacted from model output and failure diagnostics; other inherited values, such as a region or feature flag, appear as written. This controls process configuration; it does not sandbox local tools or files available to the service user.

Editing a reusable resource affects newly admitted runs and replacement seats. Accepted runs retain their nonsecret configuration snapshots. Secret references resolve at invocation time, so rotation can repair a paused run without copying secrets into its history. Disabling a secret prevents subsequent use of that secret; disabling an endpoint, model or harness prevents new admission while existing snapshots retain their accepted configuration.

## Start, interject and reach agreement

A conversation has a title, shown in conversation lists, and an optional opening question (`openingQuestion` in the API, `--question` for `conversations create` in the CLI). A debate's question defaults to the opening question, or to the title when the opening question is blank; the web start dialog shows it for editing. Each run freezes its own copy, so later edits to the conversation never change a started debate.

Each debate step's instructions come from the prompt library: factory defaults unless the conversation or the start request chooses custom prompts, frozen when the debate starts. See [PROMPTS.md](PROMPTS.md).

Web and TUI setup require two or more distinct seats and an orchestrator. Reorder the opening speakers, choose rounds and the interval between calls, then review. Capabilities advertise the configured seat maximum, 16 by default and configurable from 2 through 64. Follow-up questions remain a single-agent workflow.

The service rejects an invalid setup before it calls any model. Each rejection names the request field to correct (`field` in the API error, in parentheses in CLI output, next to the control in the web dialog, and as the setup step in the TUI). Rejections cover one seat (`too_few_participants`), more seats than the service admits (`participant_limit`), the same profile twice (`duplicate_participant`), two selected profiles whose names differ only by case or not at all (`duplicate_seat_name`), a missing or disabled participant or orchestrator (`profile_not_found`, `disabled_profile`, `missing_orchestrator`), and out-of-range rounds, countdown or limits.

The orchestrator asks a focused question. Each active participant responds; the opening order rotates by round. The orchestrator then proposes a synthesis, and every seat explicitly records `agree`, `disagree`, `abstain` or `needs_information` against that proposal. Only unanimous `agree` on the current proposal, context epoch and roster revision produces consensus. A phrase such as “WE HAVE CONSENSUS” in an answer has no special effect in protocol 2. Reaching the round limit preserves the positions without claiming consensus.

When a question, proposal, vote or summary is not the required JSON object, a vote names another proposal, or a summary fails validation, ArgueBot keeps the invalid reply as a failed turn and asks the same model once to reply again in the required format. The correction counts toward the run's limits and is marked **Correction request** in the attempt history. If the corrected reply is also invalid, the debate pauses so you can inspect both replies and resume, which tries again normally, or replace the seat or orchestrator.

While the panel is voting, **Edit proposal** (web Debate panel, TUI run commands, `debate edit-proposal` in the CLI, or the `edit_proposal` run action with the run `revision` you reviewed) replaces the proposal with your text. Every active seat then votes on the revision. Votes already cast stay with the original, which is marked as replaced, and never count toward the revision. A vote that finishes after your edit is saved is kept in the transcript but not counted. If the final vote was recorded before your edit, the debate has already finished and the edit is refused (`no_open_proposal`). The order in which the two changes are saved decides the result.

Each context submission chooses **Use in next turn** or **Interrupt now**. Next turn retains the current response but invalidates its authority to advance an outdated proposal. Interrupt now persists the context before cancelling the current invocation. Wait for cancellation acknowledgement, inspect partial work and explicitly resume. If no invocation is active, the text is queued without automatically resuming a paused run. A pinned constraint stays in original form in subsequent prompts. Private steering is visible to its recipient, not to shared orchestration or summaries.

A **private directive** (context with a recipient seat) reaches only that agent. It does not change the shared context revision, restart the round or interrupt another agent, so other agents cannot infer it from the debate state. It is delivered at the recipient's next turn; Interrupt now stops the recipient's turn only if that agent is currently speaking. If the debate would otherwise finish (unanimous, or at the round limit) before the recipient has seen the directive, only the recipient is asked to vote again with it. Its earlier vote stays visible as an earlier vote, and only its latest vote counts. The agent's changed behavior can still reveal that it was told something; the directive text, its existence in shared state and its sources are never exposed to other agents. In an open workspace, other human clients can still read every directive.

Use **Change agents** to add, remove or replace a seat at a safe boundary. Historical seats and dissent remain inspectable. A changed panel receives a new roster revision and must ratify a fresh proposal. At least two active seats are required. Concurrent edits use expected revisions and durable command keys; reload after a conflict instead of guessing the new revision.

In the web workspace, the debate panel (proposals, votes, roster, context delivery and memory) sits beside the transcript on wide screens. **Hide debate panel** and **Show debate panel** in the run bar toggle it, and the choice is remembered in the browser. On narrower screens the open panel appears above the transcript, and on phones it replaces the transcript until you hide it. A tool call appears as one card that shows its final result; the separate start record is kept in history and shown only while the call is still running or if it never finished.

The completed workspace exposes proposals and votes, context delivery receipts, source-linked summaries and clarification requests. Delivery means an original was included in a successful participant invocation; it does not prove the model understood it. After completion, further context is retained for a continuation and cannot retroactively change the recorded outcome.

## Memory and recovery

The orchestrator compacts public history when its bounded prompt cannot fit more originals. Summaries cite the supplied sources; successive compaction keeps provenance to the originals. Originals are retained and available through [conversation tools](../MCP_API.md), full-message readers and exports. Pinned constraints and recipient-only content remain original text. Inspect a summary's sources before excluding or rebuilding it. Those actions invalidate the current proposal and require fresh consideration. Rebuild compacts the original evidence again, and the current shared summary stays active until the replacement passes validation. If the rebuild fails, the debate pauses with the cause and keeps the previous summary, and resuming retries the rebuild. Exclude removes every summary from future prompts.

When compaction covers a pinned constraint, the summary's moderator constraints section must name that constraint's source ID, or the summary is rejected and the previous memory stays active. The constraint is also still delivered in its original form.

Agents can page through turns, filtered by seat or phase; search literally and case-insensitively, with snippets around each match; ask for supporting details, which report anything they cannot see as a gap; and read the debate state, including pinned constraints and positions on earlier proposals. These tools see exactly the turns the agent's own prompt context can see. See [conversation tools](../MCP_API.md).

A participant that asks a blocking clarification does not complete its turn. Its answer stays visible, but no vote or speaker advance is recorded until the moderator answers or skips the question.

Source coverage is checked mechanically. Semantic fidelity is model-dependent: a cited source can still be summarized poorly. Private history or one oversized original can exceed the configured budget; the run pauses rather than silently dropping it. Adjust the panel or source material in a new run when frozen settings cannot accommodate it.

A blocking request for clarification appears in the workspace. Answer it with an explicit delivery choice or mark it skipped, then resume. Provider failures, question/proposal/vote JSON that is still malformed after its one correction request, invalid summaries and uncertain execution also pause visibly. Resume may repeat external side effects. Correct configuration and start a new run when the accepted endpoint, harness arguments or timeout need to change.

## Scriptable interface

The CLI reads configuration and debate JSON from stdin or `--file PATH`. Secret values are refused as literal command-line arguments. All commands below use the same service as the web and TUI:

```text
configuration endpoints|harnesses|models list
configuration endpoints|harnesses|models add -
configuration endpoints|harnesses|models update ID -
secrets list|add -|update ID -
secrets disable ID REVISION
capabilities
debate start CONVERSATION -
interject CONVERSATION -
debate roster|proposals|summaries|clarifications CONVERSATION RUN
debate interjections CONVERSATION
debate change-roster CONVERSATION RUN -
debate resume|rebuild_summary|exclude_summary CONVERSATION RUN
```

Protocol 2 start document. `participantIds` and `orchestratorId` accept model IDs (`mod_...`), CLI harness IDs (`har_...`) or agent IDs (`par_...`); roster changes accept the same in `profileId`:

```json
{
  "protocolVersion": 2,
  "orchestratorId": "mod_sonnet",
  "participantIds": ["har_claude", "har_codex", "har_gemini", "mod_deepseek"],
  "question": "Which rollout design should we adopt?",
  "maxRounds": 5,
  "interventionSeconds": 10,
  "idempotencyKey": "rollout-debate-1"
}
```

Context document:

```json
{
  "runId": "run_example",
  "text": "The rollout must be reversible within five minutes.",
  "deliveryMode": "interrupt_now",
  "pinned": true,
  "idempotencyKey": "rollback-constraint-1"
}
```

Omitting `runId` targets the conversation's active run, if any. Keep the same key and identical document when retrying a command whose response was lost. Reusing a key with different text or delivery mode fails. Positional legacy `debate start CONVERSATION A B` continues to request protocol 1; use a JSON document for protocol 2.

The [REST reference](../REST_API.md), [OpenAPI contract](../assets/postman/openapi.json), [general Postman collection](../assets/postman/arguebot.postman_collection.json) and [isolated v2 acceptance workflow](../assets/postman/v2-acceptance.postman_collection.json) describe the HTTP surface. New semantics use `/v2.0/api`; old routes cannot inject ambiguous legacy notes into an active protocol 2 debate.

## Upgrade behavior

Schema 14 introduces the debate and connection tables. Upgrading a pre-v2 database creates stable reusable resources for Ollama/OpenAI inline profiles and CLI profiles without a legacy secret-environment field. Literal arguments, model IDs and timeouts are retained; historical run snapshots remain inline and unchanged. Other legacy providers and CLI environment-reference profiles remain usable through their original inline configuration. Older binaries refuse a newer incompatible schema. Internal upgrade recovery preserves the original home and its separate secret key; a database-only copy does not carry that key.

## Check a configured endpoint

In Participants, use **Check connection / list models** next to a saved endpoint. The TUI offers the same action after selecting an existing HTTP endpoint. The CLI command is `configuration endpoints discover ID`. This explicitly contacts the service-side endpoint using its saved auth bindings, with a 30-second deadline and 1-MiB response limit. It does not generate a completion or verify tool/context capabilities. Copy an advertised ID into a model configuration; manual IDs remain available.

Ollama discovery uses [its model-list contract](https://github.com/ollama/ollama/blob/main/docs/openapi.yaml); OpenAI-compatible discovery uses [the models resource](https://developers.openai.com/api/reference/cli/resources/models/methods/list) relative to the configured base URL. Error responses omit provider bodies and credentialed URLs.

Catalog GET routes accept `pageNumber` and `pageSize` (1 through 100, default 100). Shipped clients load subsequent pages. Saving configuration does not initiate discovery. A successful check identifies the checked configuration revision and time; it is not a persistent health guarantee.

Proposal and clarification history accept `pageNumber` (50 and 100 items per page respectively). The web and native clients retrieve subsequent pages so older decisions and unresolved questions remain reachable. CLI `debate summaries CONVERSATION RUN [PAGE]` and `debate interjections CONVERSATION [PAGE]` expose their bounded history pages directly.

## Execution budgets and orchestration guidance

Open **Execution budgets and orchestrator guidance** during web setup, or use the TUI wizard's settings step. Choose separate limits for participant invocations (answers and votes), orchestrator calls (questions and proposals), compactions and tools. The elapsed deadline includes queued, intervention and paused time. Limits are frozen when the run starts; explicit retries consume another reservation. A continuation can choose new limits.

The context ceiling is further restricted by the configured service and model capacities. An optional estimated-input limit uses initial prompt UTF-8 bytes plus message/tool framing allowance, rather than provider token accounting. It excludes continuation and output usage. Zero disables that estimate limit; it is not a currency cap. **Execution usage and limits** shows counters and the deadline in the workspace and TUI run controls. CLI `debate status` returns the same policy and usage.

Additional question-selection, proposal and memory guidance can be supplied independently. These instructions never grant a model authority to change budgets, membership or unanimous agreement. CLI and REST run requests supply these fields in `policy`; omitted fields use the defaults from `/v2.0/api/capabilities`.


### Endpoint capacity and private memory

Set **Concurrent generations** on an endpoint to limit simultaneous work across debates using that connection. The default is one and the maximum is 64. Waiting invocations can be interrupted or cancelled; a run's elapsed deadline continues while waiting. Capacity is an operational limit applied at admission, while accepted model, URL and authentication-binding snapshots stay frozen.

ArgueBot compacts shared evidence and each recipient's private evidence separately. The configured orchestrator model processes a private partition only for that compaction invocation. Its ordinary question/proposal context and other seats cannot retrieve the private summary. The moderator can inspect all partitions. A single source or pinned material that cannot fit still pauses explicitly.

Summary manifests show exact original-source counts and page through 100 identities at a time. In the CLI, use `debate summary CONVERSATION RUN SUMMARY_ID SOURCE_PAGE`. The summary endpoint accepts `id` and `sourcePageNumber`; model `get_summary` calls use that summary ID and offsets 0, 100, 200 and so on.

A Codex login is refreshed when its access token is about to expire. ArgueBot marks it `refresh_uncertain` (**Sign-in needed**) while the refresh request is outstanding, and keeps that status if the login service rejects the request or the result is unknown, because replaying a consumed refresh token can revoke the login. If the request never reaches the service (name resolution, connection or TLS failure) or the service answers 429 or 5xx, the login returns to ready and the run pauses with `refresh_unavailable`; resume when the service is available.

### Secret encryption and provenance

Attempt inspection lists each stored secret ID and the exact revision used. This metadata contains no token values. Replacing a token changes its authentication revision; rotating the encryption key does not.

Open **Participants → Secrets → Secret encryption keys** in the dashboard, or **Connections and models → Secret encryption keys** in the TUI. Review the active key identity, then choose **Rotate master key**. CLI equivalents are `secret-keys status` and `secret-keys rotate EXPECTED_KEY_ID`. Stale identities return a conflict instead of repeating rotation.

Rotation first decrypts every stored secret. If an enabled secret cannot be decrypted, rotation is refused before any key changes (`credential_unreadable`, naming the secret); replace it with a fresh value or disable it. Unreadable disabled secrets are left unchanged and reported as skipped.

**Retire earlier keys** (`secret-keys retire EXPECTED_KEY_ID`) removes every earlier key once all stored secrets use the active key; otherwise it is refused with `keys_in_use`. After retirement, database backups made before the latest rotation can no longer decrypt their secrets, so retire keys only when you no longer need those backups.

Rotation writes `credential.keys.json` in the protected secrets directory before reencrypting the database in a transaction. Previous keys remain in that keyring for interrupted rotations and older database backups. Existing `credential.key` installations are compatible; keep the complete protected secrets directory in a separate backup. Do not retire or delete retained keys while matching backups still exist. After rotation, back up the new keyring. A restored database without its matching key material cannot decrypt secrets.


### Explicit model verification

Select **Completion and tool checks** beside a reusable model in the dashboard, or **Observed completion and tool checks** from the TUI model actions. Saving configuration performs no generation. An explicit check invokes the configured model and can incur provider cost; selecting a tool check asks for one harmless probe and may require another completion. CLI checks use the configured harness permissions. Provider response text is discarded.

Results distinguish an observed nonempty completion from an observed tool round trip, retain model/transport revisions, and show a reported executable version when a supported CLI returns one. These observations do not certify all model capabilities or debate quality. A changed configuration does not inherit an earlier revision's verification.

The native client and REST contract expose the same checks. CLI commands are `configuration models checks ID [PAGE]` and `configuration models check ID -`, with a JSON request such as `{"idempotencyKey":"explicit-check-1","checkTools":true}` on stdin. Reusing the same key returns the original check without invoking again. Interrupted service checks become `uncertain`; use a new command only when intentionally performing another potentially billable check.

### Structured memory and delivery evidence

New compactions separate agreed facts, contested claims, participant positions, evidence, decisions, unanswered questions and moderator constraints. Every category is required, including an explicit statement when nothing is established. Malformed categories and missing or invented source references pause generation without replacing the last valid summary. Historical unstructured summaries remain readable. Structural validation does not prove semantic fidelity.

The summary inspector links earlier summaries and original turns. Opening an original shows its SHA-256 at summary acceptance. REST and native-client records expose the same `sections`, `parentSummaryIds` and paged `sources` metadata; CLI JSON and API Explorer preserve these fields. Original transcripts remain authoritative.

Context delivery distinguishes `deliveredParticipantIds` (the original was selected for a successful answer or vote) from `summarizedParticipantIds` (a source-linked summary was selected, excluding seats already counted as direct delivery). A summary reference does not prove that a model retained the original meaning. Pin an interjection when the exact original must remain in subsequent prompts. Web and TUI show both counts.

### Model generation settings

Reusable models set a service capture limit (`maxOutputBytes`, 1024 through 67108864) for both CLI and HTTP invocations. HTTP models additionally accept `generation.maxOutputTokens` (1 through 1048576), `generation.temperature` (0 through 2) and `generation.topP` (0 through 1). Omit a value or set it to null to use defaults. PolyPrompt 3.4 sends 4096 generated tokens when no token override is configured; omitted sampling values use provider defaults. Provider limits and model-specific parameter support still apply. A model check uses the saved settings and can reveal an unsupported override.

Web and TUI model editors expose these settings; CLI configuration JSON, native-client DTOs and API Explorer use the same contract. Accepted runs freeze them with the model revision, so later edits only affect new snapshots. Configure CLI-specific sampling or reasoning flags as literal harness arguments; HTTP overrides are rejected for CLI models instead of being silently ignored. Output capture bytes, provider-generated tokens and the run input estimate are different limits.

For a saved HTTP endpoint, **Preview requests** shows the normalized POST generation and GET model-list destinations and the configured header/query names with every value redacted. It reads configuration only and does not resolve secrets or contact the endpoint. Web, TUI, API Explorer, native client and `configuration endpoints preview ID` expose the same preview. Saving remains local validation; discovery and model checks are separate explicit actions.
