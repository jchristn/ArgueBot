# Prompting the user plan

Status: implemented on `feature/v2.3.0` on November 9, 2026, with open items marked `[~]` or `[ ]` below (live harness checks, simulated user testing, launcher checks, exact and native resume). Prepared November 9, 2026 against `main` at `d0b35de` (product version 2.2.0, schema 34). Target version: **2.3.0 on `feature/v2.3.0`**, requested by the product owner on November 9, 2026. CHANGELOG entries go under `Unreleased: v2.3.0`.

This plan gives agents a first-class way to stop in the middle of a turn and ask the person running the debate a question, wait for the answer, and carry on with it. A question can be open-ended text, a single choice, a multi-select or a yes/no confirmation, and one request can carry up to four questions. The person answers in a modal dialog in the web dashboard or the TUI, or from the CLI, the REST API or the C# client, and the agent resumes with the answer in the same step it asked from. Every product surface is in scope: service, database, agent tools on all three transports, prompts, REST API and generated artifacts, Postman, C# client, dashboard, TUI, CLI, desktop launchers, Docker, documentation, README, CHANGELOG and tests.

## How to use this plan

Every task has an ID (`Q<phase>.<n>`) so commits, pull requests and review comments can refer to it. Mark progress in place:

- `- [ ]` not started
- `- [~]` in progress (add your name or handle and the date after the ID)
- `- [x]` done, and only with evidence
- `- [-]` dropped, with the product owner's decision and date on the same line

Each phase ends with **Acceptance** (what must be true) and an **Evidence** line. Fill the Evidence line with the commit, the test descriptors or commands that passed, and their counts, in the same style as [archive/RESOURCES_PLAN.md](archive/RESOURCES_PLAN.md). Do not check a box without that evidence (CLAUDE.md). Update the progress table below whenever a phase changes state. Every phase that changes behavior updates its code, tests, API artifacts (`assets/api-contract.json`, `REST_API.md`, `MCP_API.md`, `assets/postman/openapi.json`, both Postman collections) and documentation together.

### Progress

| Phase | Title | Status | Commit | Evidence summary |
| --- | --- | --- | --- | --- |
| 0 | Decisions, requirements reconciliation and branch | Done | fc63f33, see Phase 0 evidence | Branch, version 2.3.0, Docker v2.3.0, requirements review, ADR 0007, Touchstone 1.0.0 with equal case counts |
| 1 | Data model and storage (schema 35) | Done | 8059cc3, final commit | SQLite and PostgreSQL storage, migration test |
| 2 | Question lifecycle in the engine | Done | 8059cc3, final commit | Hold, park, resume, restart, budgets, capacity |
| 3 | Agent tool and transports | Partial | 8059cc3, final commit | All transports; exact and native resume open |
| 4 | Prompts and intake | Partial | final commit | Intake, placeholder; prompt warning open |
| 5 | REST API, events and API artifacts | Partial | 8059cc3, final commit | Routes, SSE, contract; Explorer check open |
| 6 | C# client and dashboard API client | Partial | 8059cc3, final commit | No C# IAsyncEnumerable stream |
| 7 | Dashboard | Partial | final commit | Modal answering everywhere; attempt link and locale QA open |
| 8 | TUI | Done | 8059cc3, final commit | Modal dialogs, Questions screen, wizard |
| 9 | CLI | Done | 8059cc3, final commit | questions commands, exit code 4 |
| 10 | Desktop launchers, tray and Docker | Partial | final commit | Docs only; tray dropped |
| 11 | Postman | Done | final commit | 182 requests, 229 assertions |
| 12 | Documentation, README and CHANGELOG | Done | final commit | docs/QUESTIONS.md and updates |
| 13 | Live checks, simulated user testing and release readiness | Partial | final commit | Local verification only |

## The problem

A vague request ("make our deploys safer") gives the panel nothing to anchor on. A human expert would ask two or three questions first: which environment, what failure has hurt you, what rollback window is acceptable. ArgueBot agents have no practical way to do that today.

A primitive exists, but it does not work as a way to prompt the user:

- `request_clarification` (`src/ArgueBot.Core/Services/ConversationTools.cs`) takes one free-text `query` and a `blocking` flag. It has no choices, no multi-select, no confirmation and no way to send several related questions together.
- The tool call returns at once. The agent finishes its turn without the answer, and the answer only reaches a later invocation as pinned moderator context. The agent cannot use the answer in the reasoning it was doing when it asked.
- A blocking request pauses the run only after the turn completes (`AttemptMethods.cs`, outcome `awaiting_clarification`). After the user answers, the run stays paused until someone presses Resume (`memory.answerHelp` in `assets/i18n/en.json`).
- The prompts barely mention the tool. `DebatePrompts.Build` never describes it, and only the MCP runner appends one generic sentence. Models rarely call it.
- The dashboard shows open requests inside the Memory panel (`dashboard/src/components/MemoryPanel.jsx`). Nothing appears in the transcript, the sidebar, Home, the conversation list or the browser title, and nothing notifies the user. The TUI hides it as item 11 of the run-controls menu (`ConversationApp.cs`). The CLI can list requests but cannot answer them except through a raw `interject` body.
- Answering goes through the interjection endpoint with `clarificationId`, and the user has to choose a delivery mode for what is really an answer.
- It exists only for protocol 2 debates. There is no tenant-wide view of what is waiting for the user.
- The wall-clock budget counts from run creation (`ConversationEngine.ExecuteAsync`), so a long wait for an answer uses up the debate's time budget. Per-invocation timeouts (`ContextEnvelopeRunner`, `PolyPromptRunner`) would end a turn that waited inside a tool call.

This plan replaces that primitive with a structured, in-turn question flow, migrates its data and removes the old tool, route, field and table in 2.3.0.

## Goals and non-goals

Goals:

1. Any agent in a protocol 2 run (participant, orchestrator, follow-up answerer) can ask the user one to four structured questions in the middle of a turn, through every transport: MCP for Claude Code and Codex, the JSON envelope for Gemini CLI, Mux and generic CLIs, and native function calls for REST models.
2. The run visibly waits. The user is notified on whatever surface they are using, answers in a modal dialog with purpose-built controls, and the asking agent resumes in the same step with the answer as its tool result.
3. Waiting is cheap and safe. It does not consume the wall-clock or generation budgets, does not hold a run-capacity slot indefinitely, survives a service restart and can be stopped, skipped or deferred.
4. A debate can optionally start with an intake step in which the orchestrator asks about a vague question before round 1.
5. Every surface, artifact and document listed in the scope is updated with meaningful tests.

Non-goals:

- Protocol 1 (`/v1.0/api`) runs. They have no tool loop. The v1 API is unchanged and documented as not supporting questions.
- Agents asking each other questions. Seats already address each other through the debate.
- Collecting secrets. The UI warns against pasting credentials and the prompts tell agents never to ask for them (see Security).
- Email, chat or push delivery to services outside the local machine. No external telemetry or notification service is added (scope override of October 20, 2026).

## Decisions

The product owner confirmed these decisions on November 9, 2026, including D-16 (modals). Rows D-8, D-9 and D-13 follow the answers to O-2, O-3 and O-5. Q0.4 copies the table into `docs/REQUIREMENTS.md`.

| ID | Topic | Proposal |
| --- | --- | --- |
| D-1 | Name | User-facing name: **Questions** ("Questions for you" in navigation). Tool name `ask_user`. Entity names `QuestionRequest` (one ask, PrettyId `qrq_`), `Question` (one item, `qst_`), `QuestionOption` (`qop_`) and `QuestionAnswer` (`qan_`). |
| D-2 | Question kinds | `text` (open-ended, optional `maxLength` up to 8,000 characters, optional placeholder), `single_choice` (2 to 12 options), `multi_choice` (2 to 12 options with `minSelections` and `maxSelections`), `confirm` (yes or no). Choice kinds may set `allowOther` for a free-text "Other" answer. Options have an `id`, a `label` (up to 80 characters), an optional `description` (up to 300) and an optional `recommended` flag (at most one for `single_choice`). Every question has a short `header` (up to 40 characters, used for chips and lists), a `prompt` (up to 1,000) and a `required` flag (default true). |
| D-3 | Request shape | One `ask_user` call carries 1 to 4 questions and an optional `context` (up to 2,000 characters of markdown explaining why the agent is asking). The user answers the request as a unit. Each optional question can be skipped on its own; the whole request can be skipped. |
| D-4 | Waiting | `wait` defaults to true: the tool call does not return until the request is answered, skipped, expired or cancelled, or until the hold window ends. `wait: false` records the request and returns at once with `status: "recorded"`; the answer is delivered to that seat's next invocation as pinned context. |
| D-5 | Hold, then park | A waiting tool call is held open for up to `questionHoldSeconds` (policy, default 300, minimum 0, maximum 1,800). If the user has not answered by then, or the service restarts, or the user chooses **Answer later**, the request is *parked*: the tool returns `status: "parked"` with instructions to end the turn at once without conclusions that depend on the answer. The attempt ends in the new state `awaiting_input` and records no vote, proposal or speaker advance. When the answer arrives, the engine starts a resume attempt of the same step (same seat, phase, round and proposal) whose tool history ends with the answer as the `ask_user` result. |
| D-6 | Run state | A new run state `awaiting_input`. The run enters it while a waiting request is open, held or parked. It leaves automatically when the last waiting request is resolved (`resumeOnAnswer`, policy, default true). With `resumeOnAnswer` false the run moves to `paused` with outcome `question_answered` and waits for Resume. Stop cancels open requests (`cancelled`). |
| D-7 | Budgets | Time spent in `awaiting_input` does not count against `maxWallClockSeconds` or the participant's generation timeout. `ask_user` calls count against the existing per-turn tool budget. New policy limits: `maxQuestionRequestsPerTurn` (default 2, range 0 to 4) and `maxQuestionRequestsPerRun` (default 20, range 0 to 200). Repeating an identical open request in one attempt returns the existing request, as `request_clarification` does today. |
| D-8 | Who may ask | Policy `userQuestions`: `off`, `orchestrator` (the orchestrator and the follow-up answerer only) or `all` (every seat). Default `all` for new debates and for upgraded debates, since every seat can already call `request_clarification` (O-2). With `off` the tool is not offered. |
| D-9 | Intake | Policy `intake`: `off` or `ask_if_needed`, with `maxIntakeQuestions` (default 4). With `ask_if_needed`, a new debate starts in phase `intake`: the orchestrator reads the question and either asks up to `maxIntakeQuestions` questions in one request or proceeds. Answers become pinned constraints for the whole panel. Default `ask_if_needed` for new debates (O-3) and `off` for upgraded debates. |
| D-10 | Answer visibility | Each request sets `visibility`: `panel` (default; the answer becomes a pinned moderator message every seat sees) or `self` (only the asking seat sees it). The answer is stored as a transcript message of kind `answer` linked to the request, so the evidence tools, summaries and transcripts treat it like other moderator context. |
| D-11 | Who may answer | Any member of the tenant who can open the conversation (no RBAC; Member access class), matching how interjections work today. The answering user's ID is recorded. The inbox defaults to conversations the user created and offers "All in this tenant". |
| D-12 | Expiry | Policy `questionExpirySeconds` (default 0, meaning never). An expired request resolves as skipped with reason `expired`, and the agent is told to continue on stated assumptions. |
| D-13 | Compatibility | Removed in 2.3.0 (O-5). Schema 35 migrates every `debate_clarifications` row into the new tables and then drops `debate_clarifications`. The `request_clarification` tool, `GET .../runs/{runId}/clarifications`, `clarificationId` and `skipClarification` on interjections, the `clarification_required` error, `IClarificationMethods`, `ClarificationRequest` and `ArgueBotClient.ClarificationsAsync` are deleted. Upgraded debates that were `paused`/`awaiting_clarification` become `awaiting_input` with a parked request. CHANGELOG lists all of these under breaking changes with their replacements. |
| D-14 | Live channel | The existing conversation SSE stream (`GET /v2.0/api/conversations/{id}/events`) gains `question` events. A new tenant-scoped SSE stream (`GET /v2.0/api/questions/events`) feeds inbox badges and notifications, with polling of `GET /v2.0/api/questions/summary` as the fallback. SSE is the product's existing live channel, so this is recorded as a deviation from the WebSocket wording in BACKEND_ARCHITECTURE.md (see Requirements compliance). |
| D-15 | Native harness question tools | Claude Code's built-in `AskUserQuestion` and any equivalent in other harnesses cannot reach a person in a headless invocation. Invocations disable them where the harness allows it and the prompt steers agents to `ask_user`. Phase 3 verifies the exact flag per harness version and records it in docs/EXECUTION.md. |
| D-16 | Modal answering | Answering always happens in a modal dialog, never in an inline form. In the dashboard, the transcript, banner, inbox, Home card, toasts and browser notifications only show a compact read-only record with an **Answer** action that opens the question modal. The modal opens automatically when the user is viewing a conversation whose run enters `awaiting_input`. With several open requests the modal steps through them ("1 of 3"). Closing the modal (ESC, backdrop or Close) leaves the request open and is not a skip. If another user resolves the request while the modal is open, the modal shows who resolved it and offers Close. The TUI uses its modal `QuestionDialog` the same way. |

### Product owner answers

Recorded November 9, 2026.

| ID | Question | Answer |
| --- | --- | --- |
| O-1 | Target version and branch | 2.3.0 on `feature/v2.3.0`. Phase 0 changes the version. |
| O-2 | Default `userQuestions` on new debates | `all` (every seat, bounded by limits). |
| O-3 | Default `intake` on new debates | `ask_if_needed`. Upgraded debates stay `off`. |
| O-4 | Tray companion app | No. Desktop notifications come from the dashboard through the browser Notification API. Tray tasks Q10.5 to Q10.9 are dropped. |
| O-5 | Removal of `request_clarification` and clarification adapters | Remove now, in 2.3.0 (breaking change). |
| O-6 | Touchstone 1.0.0 (requirements commit `11a3982`; repository on 0.2.1) | Upgrade first, in Phase 0 (Q0.7), before any new descriptor is written. |
| O-7 | SDK under `sdk/{language}` with a CI harness | Record a deviation: `src/ArgueBot.Client` stays the C# SDK, and Test.Shared client tests cover questions. |
| O-8 | Questions and answers in shared transcripts | Yes, always, like other moderator messages, with no share option. |
| UI | How the user is prompted | Through modals, not inline (D-16). |

## Behavior specification

### Tool contract: `ask_user`

Arguments (own JSON schema, not the shared evidence schema; `additionalProperties: false` throughout):

```json
{
  "context": "Your request names deploys but not the environment or failure mode, and the answer changes which rollback design fits.",
  "questions": [
    {
      "key": "environment",
      "header": "Environment",
      "prompt": "Which environment should the panel design for?",
      "kind": "single_choice",
      "options": [
        { "id": "prod", "label": "Production only", "recommended": true },
        { "id": "all", "label": "Every environment", "description": "Staging and development follow the same rules" }
      ],
      "allowOther": true
    },
    {
      "key": "pain",
      "header": "Failures",
      "prompt": "Which failures have hurt you most?",
      "kind": "multi_choice",
      "minSelections": 1,
      "maxSelections": 3,
      "options": [
        { "id": "migrations", "label": "Database migrations" },
        { "id": "config", "label": "Configuration drift" },
        { "id": "capacity", "label": "Capacity during rollout" }
      ]
    },
    { "key": "window", "header": "Rollback", "prompt": "What rollback window is acceptable?", "kind": "text", "maxLength": 500, "required": false }
  ],
  "wait": true,
  "visibility": "panel"
}
```

`key` is chosen by the model (1 to 32 characters, unique in the request) so it can match answers to questions without parsing IDs.

Results (all inside the existing `{ "success": true, "result": ... }` envelope):

| `status` | When | Other fields |
| --- | --- | --- |
| `answered` | The user answered (some optional questions may be skipped) | `requestId`, `answeredUtc`, `answers[]` with `key`, `kind`, `selected` (option IDs), `selectedLabels`, `otherText`, `text`, `confirmed`, `skipped` |
| `skipped` | The user skipped the whole request, or it expired | `requestId`, `reason` (`skipped_by_user`, `expired`), optional user note, and the instruction to continue on stated assumptions |
| `parked` | The hold window ended, the user chose Answer later, or the service is restarting | `requestId` and the instruction to end the turn now in one short sentence, without conclusions that depend on the answer |
| `recorded` | `wait: false` | `requestId`; the answer arrives as pinned context in a later invocation |
| `cancelled` | The run was stopped while waiting | `requestId`; the turn ends |

Errors use the existing `{ "success": false, "code", "error" }` shape and are tool errors, not transport failures (SIMULATED_USER_TESTING.md, surface checks): `user_questions_disabled`, `invalid_question` (with a field path), `question_limit` (per turn or per run), `intake_question_limit`, `tool_budget_exhausted`.

### Lifecycle

Request status: `open` → `answered`, `skipped`, `expired` or `cancelled`. While `open`, a waiting request is either `held` (a live tool call is waiting) or `parked` (no live call; a resume attempt follows the answer). Answering is first-writer-wins: a second answer or skip returns 409 `question_already_resolved` with the current resolution.

Run: `agent_turn` → `awaiting_input` when a waiting request opens → `agent_turn` (held call returns the answer) or `queued` (parked; a resume attempt is scheduled) when the last waiting request resolves. `stop` from `awaiting_input` cancels open requests and ends as `cancelled`/`stopped`. `pause` from `awaiting_input` parks held requests and moves the run to `paused`; answering then does not auto-resume.

Attempt: a parked attempt ends with state `awaiting_input` and the request ID. Its partial output is kept as a non-final draft that no seat votes on. The resume attempt links to it (`resumesAttemptId`) and appears in `debate attempts` and the dashboard attempt dialog.

### Resume per transport

| Transport | Held answer | Parked resume |
| --- | --- | --- |
| REST models (`PolyPromptRunner`) | The tool result is the answer; the tool loop continues. | Replay the stored request history (instructions, prior tool calls and results) and append the answer as the `ask_user` result. The attempt stores this continuation as a typed record. |
| JSON envelope (`ContextEnvelopeRunner`) | The envelope continuation already re-invokes the CLI per tool round, so the answer is the next continuation entry. | The same continuation list is persisted and replayed with the answer appended. |
| MCP bridge (`McpInvocationRunner`, Claude Code and Codex) | The bridge holds the MCP call open; the CLI waits. The bridge raises the harness tool timeout above the hold window (Claude Code `MCP_TOOL_TIMEOUT`, Codex `tool_timeout_sec`; exact names verified in Q3.6). | Use the harness's native session resume when available (session ID captured from the stream; verified in Q3.7), passing the answer as the next user message. Otherwise start a fresh invocation whose context includes the draft, the question and the answer. |

### Waiting, budgets and capacity

- The per-invocation deadline becomes a pausable deadline. Runners stop the clock while an `ask_user` call is held and restart it when it returns.
- The run records `waitingSeconds`. The wall-clock deadline is `createdUtc + maxWallClockSeconds + waitingSeconds`.
- While a call is held, the run releases its service run-capacity slot and reacquires it before continuing, so one waiting run cannot block others. A service setting `maxLiveQuestionHolds` (default 4, range 0 to 64) bounds held CLI processes; above it, new waiting requests are parked at once.
- On startup, the service marks every held request as parked and every run in `awaiting_input` stays there without a live call.

### Security and trust

- Question text, context and option labels are model output. Every surface renders them as text or sanitized markdown (no raw HTML, links not followed automatically) and labels them with the asking seat.
- Prompts tell agents never to ask for passwords, tokens, keys or other secrets. The answer form shows a short notice that ArgueBot never needs secrets in answers. The service flags (does not block) prompts that look like credential requests so the UI can show a stronger warning; the flag is a stable code, not rendered text (I18N.md).
- The tenant of a request comes from the run, never from tool input. Reads and answers are tenant-scoped; another tenant's request ID returns 404.
- The tool surface stays invocation-scoped and exposes no answering or listing of other runs' questions (MCP_API.md trust rules).

## Requirements compliance

Phase 0 re-reads every normative document in `~/Code/Agents/requirements`, confirms hashes against `docs/requirements-manifest.json` and records deviations in `docs/REQUIREMENTS.md` before implementation. On November 9, 2026, every hash matched except BACKEND_TEST_ARCHITECTURE.md (O-6). This table maps each document to the parts of the plan that satisfy it.

| Document | What applies | Where |
| --- | --- | --- |
| BACKEND_ARCHITECTURE.md | PrettyId string IDs; per-feature route registrar with `OpenApiRouteMetadata`; `ctx.Token` everywhere; typed request and response DTOs in their own files; typed page filter and page result (not dictionaries); structured persistence with child tables for questions, options and selections (no state JSON blob); a domain `IQuestionMethods` on `DatabaseDriverBase` with tenant-scoped methods; versioned, idempotent, additive migrations in both providers; serialized SQLite writes for answer races; a service class only for real orchestration (the lifecycle qualifies); provider matrix suites for migrations, tenant enumeration, authorization-sensitive reads and concurrent writes | Phases 1, 2, 5 |
| AUTHENTICATION.md | Tenant in every WHERE clause, set from context; 404 for other tenants' IDs; cascade delete with run, conversation and tenant; `active` and UTC timestamps on every row; static route-to-access-class mapping; 409 for already-resolved; client-side checks are UI-only | Phases 1, 5 |
| BACKEND_TEST_ARCHITECTURE.md | Touchstone 1.0.0 (upgraded in Q0.7); shared descriptors in Test.Shared with no console output; self-contained data; `127.0.0.1` only, including MCP clients; full suite on every commit; PostgreSQL provider job; new descriptors in the 1.0 shape (`skipReason`, explicit cancellation tokens, specific exceptions) | Q0.7 and all test tasks |
| FRONTEND_ARCHITECTURE.md | React, Vite, React Router; named methods on the shared API client with `encodeURIComponent`; toasts through the shared notification state; modal focus trap, ESC and scroll lock; Explorer handles the new SSE stream; responsive at 1280, 768 and 390 px; i18n through the runtime | Phases 6, 7 |
| DASHBOARD_STYLE_AND_USABILITY.md | Route inventory row for each new route; normalized page and error shapes; table toolbar, paging, sizes and refresh; row actions (View, View JSON); custom modal dialogs only (no `alert`, `confirm`, `prompt`), which D-16 uses for every answer; purpose-built forms with inline validation; live updates with polling fallback and a visible live indicator; refresh paused while a modal is open; alerts link to their record; landmarks, labelled icon buttons, visible focus, color never the only signal, reduced motion; empty, error and loading states; visual QA at three viewports in both themes | Phase 7 |
| I18N.md | Stable keys with ICU plurals and interpolation; display labels for kinds and statuses; `Intl.ListFormat` for selected-option lists; semantic event payloads, not rendered strings; CI checks for missing, orphaned and hard-coded strings; pseudo-locale (en-XA) and RTL (ar-XB) coverage; agent-authored text documented as untranslated | Phases 5, 7, 8 |
| SIMULATED_USER_TESTING.md | A full session after the feature is complete, on an isolated `arguebot-usertest` instance at `127.0.0.1`, covering lifecycle, cross-surface, permission and restart journeys, double submit and two-tab races, expiry, MCP tool errors, CLI stdout, stderr and exit codes; findings as UT-nnn with severity; fixes chosen by the user, each with a regression test and CHANGELOG entry | Phase 13 |
| TELEMETRY_REQUIREMENTS.md (local parts) | In-process meters and spans only, no IDs or free text in labels: `arguebot.questions.asked` (by kind and asker role), `arguebot.questions.resolved` (by outcome), `arguebot.questions.wait.duration` (seconds), spans `question.ask`, `question.answer`, `question.resume` | Phase 2 |
| BENCHMARKING.md | Optional black-box measurement of answer-to-resume latency through public routes; not a CI gate | Q13.6 |
| WRITING_DOCUMENTS.md, CODE_STYLE.md | No em-dashes anywhere (docs, Postman descriptions, comments, commits); no stock openers or filler; sections carry prose; configurable values as public members with documented defaults, minimums and maximums; `<exception>` tags; no console output in libraries | All phases |
| VERSIONING.md | No version change without an explicit request; `Unreleased` in CHANGELOG | Phase 0, Phase 12 |
| REPOSITORY_REQUIREMENTS.md | CHANGELOG entry; REST_API.md covers every endpoint with parameters, bodies, codes, access and examples; Postman collection with collection, folder and request descriptions and variables; MCP_API.md covers each tool's schema, output and examples; SDK layout recorded as a deviation (O-7) | Phases 5, 6, 11, 12 |
| INSTALLERS.md | No new installed surface (O-4: no tray app); launchers keep their existing notices | Phase 10 |
| EXAMPLE_APPLICATIONS.md | Reference only (multi-tenant scoping tests, suite registries, dashboard notifications); the requirement documents win on conflict | Reference |

Deviations to record in `docs/REQUIREMENTS.md` in Q0.4 (each is existing practice or a forced choice):

- SSE, not WebSocket, is the live channel (D-14), matching the existing conversation stream.
- IDs use the existing `Ids.New(prefix)` helper rather than a central `Constants.cs` and `IdGenerator`.
- Routes stay under `/v2.0/api/...`, the product's established prefix.
- Access classes stand in for RBAC operation tuples (November 4, 2026 scope): listing and reading questions is Member read, answering, skipping and deferring is Member write.
- The C# SDK stays at `src/ArgueBot.Client` without a separate `sdk/csharp` harness; Test.Shared client tests run in CI instead (O-7).

## Current state

- Tool catalogue: `ConversationTools.Definitions(DebatePolicy)` returns the evidence tools, `request_clarification` and the resource tools from `ResourceTools`. `ExecuteAsync` validates, reserves budget through `Budgets.ReserveToolAsync` and dispatches.
- Storage: `debate_clarifications` is created in `src/ArgueBot.Core/Database/MemorySchema.cs`; `IClarificationMethods` offers `HasBlockingAsync` and `EnumerateAsync`; `InterjectionMethods` resolves a clarification when an interjection carries `clarificationId`. Schema version 34 (`DatabaseSchema.CurrentVersion`).
- Engine: `ConversationEngine.ExecuteAsync` runs one step per loop iteration, holds `_Capacity` for the whole run, applies `MaxWallClockSeconds` from `CreatedUtc` and routes protocol 2 tool runs through `IConversationToolRunner.GenerateWithContextAsync`. `resume` refuses with `clarification_required` while a blocking request is open.
- Runners: `McpInvocationRunner` (Claude Code and Codex over a named-pipe MCP bridge), `ContextEnvelopeRunner` (re-invokes the CLI once per tool round, up to 16, under `profile.TimeoutSeconds`), `PolyPromptRunner` (REST streaming tool loop).
- Prompts: `DebatePrompts.Build`; policy in `src/ArgueBot.Core/Models/DebatePolicy.cs`.
- API: `DebateRoutes.cs` registers `GET .../runs/{runId}/clarifications`; the conversation SSE stream is in `ConversationRoutes.cs`. Contract in `assets/api-contract.json`, generated by `scripts/generate-api.mjs`; acceptance workflow from `scripts/generate-v2-workflow.mjs`.
- Clients: `ArgueBotClient.ClarificationsAsync` (C#), `client.clarifications` (dashboard).
- Dashboard: `MemoryPanel.jsx` answer modal; navigation in `App.jsx` (`navigationGroups`).
- TUI: `ConversationApp.AnswerClarificationAsync` behind run controls; `ChoiceDialog`, `TextEditorDialog`, `FormDialog` available.
- CLI: `debate clarifications CONVERSATION RUN` in `src/ArgueBot.Cli/Program.cs`.
- Tests: `Runners.ContextMemory` covers blocking clarification pauses (`ContextChecks.cs`); `scripts/test-cli-surfaces.mjs` lists clarifications.
- Desktop: `packaging/` holds browser and terminal launchers for macOS, Windows and Linux and a systemd user unit. No tray app exists.
- Postman: `assets/postman/*.json` (generated), `postman/mocks/ollama`, `postman/simulations/runners.sim.yaml`.

## Phase 0: Decisions, requirements reconciliation and branch

- [x] Q0.1 Review the Decisions table and open questions O-1 to O-8 with the product owner. Evidence: answers recorded November 9, 2026 in "Product owner answers", plus the modal requirement (D-16).
- [x] Q0.2 First step: create and enter `feature/v2.3.0` from `main`. All later work happens on this branch. Evidence: branch created from `d0b35de` on November 9, 2026.
- [x] Q0.2a Move the product version from 2.2.0 to 2.3.0 everywhere 2.2.0 appears as the product version, following the checklist in archive/RESOURCES_PLAN.md Phase 0 (build props, dashboard package files and sidebar, API generators, telemetry meter version, splash screen, token routes, test assertions, project and packaging locks, dependency inventory, README note). Regenerate API artifacts and justify every remaining `2.2.0` hit. Evidence: commit fc63f33. Remaining `2.2.0` hits are CHANGELOG history, the README "v2.2.0 added" line, historical statements in docs/GENERATION_DIAGNOSTICS.md, docs/MULTIPLE_AGENTS.md, docs/REQUIREMENTS.md, docs/RESOURCES.md and DOCKERHUB_README.md ("Resources (v2.2.0)"), and third-party package versions in `assets/dependencies.json` and `dashboard/package-lock.json`.
- [x] Q0.2b Docker assets point at `v2.3.0` images: `docker/compose.yaml` uses `image: jchristn77/arguebot:v2.3.0`; `docker/compose.build.yaml`, `docker/update.sh`, `docker/update.bat`, `docker/README.md`, `DOCKERHUB_README.md` and the examples in `build-all.sh` and `build-all.bat` name `v2.3.0`. Acceptance: `docker compose -f docker/compose.yaml config` shows `jchristn77/arguebot:v2.3.0`. Building and pushing the image stays in Q13.7. Evidence: commit fc63f33; `docker compose -f docker/compose.yaml config` prints `image: jchristn77/arguebot:v2.3.0`. `compose.build.yaml`, `update.sh` and `update.bat` name no tag (they build `arguebot:local` or pull what `compose.yaml` names), so they needed no change.
- [x] Q0.3 Re-read all normative documents, confirm hashes, and for BACKEND_TEST_ARCHITECTURE.md record the reviewed-to-current change (Touchstone 1.0.0: new adapters, `ITestSuiteProvider`, `skipReason`, tags, `ConsoleRunner.RunMainAsync`, versioned JSON results, exit codes). Update `docs/requirements-manifest.json` after the review. Evidence: all other hashes matched; the change is recorded in docs/REQUIREMENTS.md and the manifest now records `f653dca3...` with `reviewedUtc` 2026-11-09.
- [x] Q0.4 Add a "Questions scope" section to `docs/REQUIREMENTS.md` with the confirmed decisions and the deviations listed above.
- [x] Q0.5 Write `docs/adr/0007-user-questions.md`: the hold-then-park model, why the old primitive is replaced, the per-transport resume strategy and the alternatives rejected (always park, always hold, separate pre-debate form only).
- [x] Q0.6 Start the CHANGELOG `Unreleased: v2.3.0` section above 2.2.0 with a short introduction that links to this plan and a "Breaking changes" heading for the clarification removal.
- [x] Q0.7 Upgrade Touchstone from 0.2.1 to 1.0.0 before any new descriptor is written (O-6): reference Core, Cli and the adapters individually; move `Test.Xunit` to `Touchstone.Xunit3Adapter` (or `Xunit2Adapter` with a recorded reason) and `Test.Nunit` to the 1.0 `NunitAdapter`; make each suites class a `public sealed` `ITestSuiteProvider` whose `All` returns fresh descriptors; replace skip flags with `skipReason`; switch `Test.Automated` to `ConsoleRunner.RunMainAsync`, stripping `--provider` before the call; read results from the versioned JSON `results` array in any script that parses them; update CI to run Test.Automated per framework and every host with no filtering flags. Record the upgrade in CHANGELOG and docs/DEVELOPMENT.md. Evidence: `Test.Xunit` became `Test.Xunit3` (Touchstone.Xunit3Adapter 1.0.0, xunit.v3.mtp-off 4.0.2 so `dotnet test` and TRX keep working), NUnit uses Touchstone.NunitAdapter 1.0.0, and both hosts have a run-all test and per-case tests in one serialized collection. `AllSuites` is an `ITestSuiteProvider`, every skip uses `skipReason` only, and `Test.Automated` targets net8.0 and net10.0 and calls `ConsoleRunner.RunMainAsync`. ArgueBot has no `--provider` argument (PostgreSQL comes from `ARGUEBOT_TEST_POSTGRESQL`), and no script parses the JSON results (scripts only pass `--suite` and `--results`). CI runs the console suite per framework and the renamed host.

Acceptance: answers recorded; the manifest matches the reviewed hashes; the REQUIREMENTS.md section and ADR are committed; `dotnet build` has no warnings; the full existing suite passes on Touchstone 1.0.0 with the same case count as before the upgrade (no case silently dropped), on SQLite and PostgreSQL, in every host; the Terminal and packaging suites pass with 2.3.0.

Evidence: Release build 0 warnings, 0 errors. With 2.3.0: Packaging 1/1, Terminal 22/22. Baseline on Touchstone 0.2.1 (net10.0 console): 94 cases, 68 passed, 26 skipped, 0 failed. On 1.0.0: console net10.0 94 (68 passed, 26 skipped); console net8.0 72 (46 passed, 26 skipped; the 22 Terminal cases build only for .NET 10); Test.Xunit3 net8.0 73 and net10.0 95 (the per-case rows plus one run-all test, 0 failed); Test.Nunit net8.0 73 and net10.0 95 (0 failed). The 26 skips are environment-gated fixtures (CLI fixture, Postman simulation, PostgreSQL), the same set before and after; the PostgreSQL provider job and the fixture-backed integration job run them in CI.

## Phase 1: Data model and storage (schema 35)

- [x] Q1.1 Tables, each with `id`, `tenant_id`, `active`, `created_utc`, `updated_utc`, composite tenant indexes and cascades from `runs` (and so from conversations and tenants):
  - `question_requests`: `conversation_id`, `run_id`, `attempt_id`, `call_id`, `asker_profile_id`, `asker_role` (`participant`, `orchestrator`, `followup`), `phase`, `round`, `context`, `wait`, `visibility`, `status`, `hold_state` (`held`, `parked`, `none`), `resolution_reason`, `answered_by_user_id`, `answer_message_id`, `resume_attempt_id`, `credential_warning`, `expires_utc`, `resolved_utc`, `UNIQUE(run_id, call_id)`.
  - `questions`: `request_id`, `position`, `key`, `header`, `prompt`, `kind`, `allow_other`, `min_selections`, `max_selections`, `max_length`, `placeholder`, `required`.
  - `question_options`: `question_id`, `position`, `option_key`, `label`, `description`, `recommended`.
  - `question_answers`: `question_id`, `skipped`, `text`, `other_text`, `confirmed`, `answered_by_user_id`.
  - `question_answer_selections`: `answer_id`, `option_id`.
  - Add both providers' DDL and an upgrade step 34 → 35 in `SchemaDefinition.cs` and the PostgreSQL schema.
- [x] Q1.2 Migrate every `debate_clarifications` row into one request with one `text` question, keeping its status, answer message link and timestamps, then drop `debate_clarifications` in the same migration (D-13). Runs that were `paused`/`awaiting_clarification` become `awaiting_input` with their open blocking request parked.
- [x] Q1.3 Policy fields on `DebatePolicy` with validation and documented defaults and ranges: `userQuestions`, `intake`, `maxIntakeQuestions`, `questionHoldSeconds`, `questionExpirySeconds`, `resumeOnAnswer`, `maxQuestionRequestsPerTurn`, `maxQuestionRequestsPerRun`. Store them in the policy table columns; upgraded debates get the D-8 and D-9 upgrade defaults.
- [x] Q1.4 Run fields: `waitingSeconds`, `awaitingSinceUtc`. Attempt fields: `questionRequestId`, `resumesAttemptId`, and the typed continuation record needed for parked resume (Q2.6). Add the new run state `awaiting_input` and attempt state `awaiting_input` to every state check that lists states (search for `"paused"` and `"window"` in Core, Server, TUI, CLI and dashboard).
- [x] Q1.5 Models in `src/ArgueBot.Core/Models`, one type per file, XML-documented, null-checked setters: `QuestionRequest`, `Question`, `QuestionOption`, `QuestionAnswer`, `QuestionKind` and `QuestionStatus` constants, `QuestionAnswerRequest`, `QuestionSkipRequest`, `QuestionFilter` (page number, page size, status, conversation, run, scope `mine` or `tenant`), `QuestionPage`, `QuestionSummary` (open counts).
- [x] Q1.6 `IQuestionMethods` and `QuestionMethods`: create (from a validated tool request), read by ID, enumerate by filter (paged, tenant-scoped, newest open first), count open by tenant and by user, answer (first writer wins inside one transaction; 409 `question_already_resolved`), skip, park, mark held, expire due requests, cancel open requests for a run, link answer message and resume attempt. Expose it on `DatabaseDriverBase`. Delete `IClarificationMethods`, `ClarificationMethods` and `ClarificationRequest`, and remove the clarification branch from `InterjectionMethods` and the `awaiting_clarification` path from `AttemptMethods`.
- [x] Q1.7 Validation: kinds, option counts, lengths, one `recommended` per single choice, `minSelections` ≤ `maxSelections` ≤ option count, unique keys, NUL bytes, total request size (16 KiB), answer conformance (selected options belong to the question, counts within bounds, required questions answered unless the request is skipped).
- [x] Q1.8 Audit answer, skip, defer and cancel events with request and conversation IDs, never answer text.

Acceptance: storage tests on SQLite and PostgreSQL for every method, including tenant isolation (another tenant gets 404 by ID and sees nothing in enumeration or counts), concurrent answers from two users (exactly one wins), validation errors, cascade on run, conversation and tenant delete, expiry, and the 34 → 35 upgrade from a fixture database containing open, answered and skipped clarifications.

Test descriptors: `Storage.Questions`, `Storage.QuestionMigration`, `Providers.PostgresqlQuestions`.

Evidence: commits 8059cc3 and the Phase 2 to 12 commit listed in the execution record. `Runners.QuestionStorage` (validation with field paths, IDs, credential flag, call-ID and per-turn and per-debate limits, tenant isolation by ID, list and counts, two concurrent answers with exactly one winner, stored selections, answer message, inbox order, filters, change feed, cancel, cascade on conversation delete), `Storage.QuestionMigration` (a schema 34 database with open, answered and skipped clarifications upgrades, the waiting run becomes `awaiting_input`, the table is dropped, and answering resumes the debate) and `Providers.PostgresqlQuestions` on a disposable PostgreSQL 127.0.0.1 cluster (`scripts/test-postgresql.mjs`: 6/6 provider cases) pass.

## Phase 2: Question lifecycle in the engine

- [x] Q2.1 `QuestionCoordinator` in Core: owns held calls (one awaitable per open waiting request), wakes them on answer, skip, expiry, cancel or park, and is the only path that changes request status from the engine side. Thread-safe, cancellation-aware, no console output.
- [x] Q2.2 Run state transitions from the Lifecycle section, under the existing conversation gate, including `pause`, `stop`, `interrupt` and `resume` from `awaiting_input`. Replace the `clarification_required` check on `resume` with `question_pending` (409) and remove `clarification_required`.
- [x] Q2.3 Pausable invocation deadline: a small `InvocationDeadline` type passed to runners, stopped while a call is held. Wall-clock deadline extended by `waitingSeconds`.
- [x] Q2.4 Release and reacquire the run-capacity slot around a held call. Enforce `maxLiveQuestionHolds` (service setting, also in `SettingsView` and `SettingsUpdate`).
- [x] Q2.5 Park on hold expiry, on **Answer later**, on `pause`, on service shutdown and on startup recovery. A parked attempt completes as `awaiting_input` with no vote, proposal, speaker advance or transient-retry count.
- [x] Q2.6 Resume: when the last waiting request of a parked run resolves and `resumeOnAnswer` is true, queue the run and start a resume attempt of the same step with the stored continuation and the answer. With `resumeOnAnswer` false, move to `paused`/`question_answered`.
- [x] Q2.7 Answer delivery: write the answer as a pinned moderator message of kind `answer` (visibility `panel` or recipient = asker for `self`), link it to the request, and make it visible to evidence tools, summaries and the transcript. `wait: false` answers reach the asker's next invocation.
- [x] Q2.8 Expiry sweep on a timer and on read; expired requests resolve as skipped/`expired` and wake or resume as above.
- [x] Q2.9 Follow-up mode: the single answerer may ask; the same wait and resume rules apply.
- [x] Q2.10 Events: append `question` events (`opened`, `held`, `parked`, `answered`, `skipped`, `expired`, `cancelled`) with IDs and status only, so clients render their own text (I18N.md). Publish to the conversation stream and the tenant question stream.
- [x] Q2.11 Telemetry meters and spans listed in Requirements compliance, through `ProductTelemetry`.
- [x] Q2.12 Logs at Information for open, resolve and resume with IDs and durations, never question or answer text.

Acceptance: engine tests with the Test.Fixtures runners for held answer, hold expiry then park then resume, Answer later, pause and resume from `awaiting_input`, stop while held and while parked, service restart while held (request becomes parked, run resumes after answer), expiry, `resumeOnAnswer` false, wall-clock exclusion (a run that waits longer than its wall-clock budget still completes), capacity release (a second run progresses while the first waits), `maxLiveQuestionHolds` overflow parks immediately, follow-up mode, and that a parked attempt never records a vote.

Test descriptors: `Runners.QuestionLifecycle`, `Runners.QuestionResume`, `Runners.QuestionBudgets`.

Evidence: `Runners.QuestionLifecycle` (held answer in the same turn; resume refused while waiting; Answer later parks and the answer resumes the step with the resume instruction and a linked attempt; a second answer conflicts; pause, then answering does not resume and Resume is refused with `question_pending` while open; stop cancels; a graceful engine stop parks the held question and a new engine resumes it after the answer; `{answered_questions}`) and `Runners.QuestionBudgets` (a 3-second wall-clock budget survives a wait of 2 seconds or more; a second run completes on a one-slot engine while the first waits; `MaxLiveQuestionHolds = 0` parks at once; skip and expiry resume with the right message; `off`, orchestrator-only and compaction offer no tool) pass. `Runners.ContextMemory` was rewritten for `ask_user` and passes.

## Phase 3: Agent tool and transports

- [x] Q3.1 `QuestionTools` (sibling of `ResourceTools`) with the `ask_user` definition, its own schema and validation, bound to the invocation's tenant, run, seat, role, phase and attempt. `ConversationTools.Definitions(policy)` includes it according to `userQuestions` and the seat's role.
- [x] Q3.2 Remove `request_clarification` from the catalogue, `ConversationToolArguments.Blocking` and its schema property (D-13). A model that still calls it gets the standard `unknown_tool` tool error.
- [x] Q3.3 Limits: per-turn and per-run request counts, intake limits, the existing tool budget, duplicate detection inside an attempt.
- [~] Q3.4 REST transport: the held call awaits the coordinator inside the tool loop; the parked path stores the continuation; the resume path replays it. Status: Held answers return through the REST tool loop. A parked REST turn resumes with a reconstructed context (transcript with the draft and the answer, plus the resume instruction); exact replay of the stored request history is not implemented, and `attempt_continuations` is reserved for it.
- [~] Q3.5 JSON envelope transport: the same, using the continuation list; document the envelope result shape for `ask_user`. Status: Held answers return as the next envelope continuation. A parked envelope turn resumes with a reconstructed context; exact replay is not implemented.
- [~] Q3.6 MCP bridge: hold the MCP call open; set the harness tool timeout above `questionHoldSeconds` for Claude Code and Codex. Verify the variable or config key against the installed harness versions and record them in docs/EXECUTION.md. Check the MCP frame limit covers the largest request. Status: `MCP_TOOL_TIMEOUT` (Claude Code) and `mcp_servers.arguebot.tool_timeout_sec` (Codex) are set to 1,860 seconds and checked by the CLI fixture in `Runners.QuestionTransports`. Verification against installed harness versions waits for Q13.1.
- [ ] Q3.7 Native session resume for Claude Code and Codex: capture the session ID from the stream, resume with the answer as the next message, and fall back to a reconstructed context when resume is unavailable or fails. Record which harness versions support which path. Status: Not done: parked Claude Code and Codex turns resume with a reconstructed context, not native session resume.
- [x] Q3.8 D-15: disable built-in ask tools in headless harness invocations where supported; test that the arguments are present. Status: Claude Code gets `--disallowedTools AskUserQuestion`, checked by the fixture. No Codex equivalent is known; none is passed.
- [x] Q3.9 Tool activity rows: the transcript shows `ask_user` start and result as one card like other tools, and a dedicated question card (Phase 7) links to it.
- [x] Q3.10 `MCP_API.md`: add `ask_user` (schema, every result status, errors, limits, visibility, wait and park semantics, examples for each kind), remove `request_clarification` and its example, and update the transport sections.

Acceptance: fixture tests through each transport (MCP in Claude Code and Codex shapes using `McpClientFixture`, JSON envelope, REST against the provider simulation) for answered, skipped, parked then resumed, `wait: false`, validation errors returned as tool errors, limits, `request_clarification` returning `unknown_tool`, `userQuestions: off` offering no tool, and `orchestrator` hiding it from participants. Rewrite the clarification cases in `Runners.ContextMemory` (`ContextChecks.cs`) as question cases.

Test descriptors: `Runners.QuestionTools`, `Runners.QuestionTransports`, `Runners.QuestionRest`.

Evidence: `Runners.QuestionTransports` (CLI fixture: Claude Code and Codex MCP shapes with `MCP_TOOL_TIMEOUT`, `tool_timeout_sec` and `--disallowedTools AskUserQuestion` checked, invalid arguments as a tool error, the held answer returned; envelope CLI answered; `off` offers nothing) and `Runners.QuestionRest` (simulation model `multi-agent-questions`, held with a 60-second hold and parked with 0, both finishing with the answer) pass. Not done: exact replay and native session resume (Q3.4, Q3.5, Q3.7).

## Phase 4: Prompts and intake

- [x] Q4.1 `DebatePrompts.Build` describes `ask_user` when it is offered: ask only when the answer would materially change the outcome and cannot be found in the conversation or resources; prefer choices with a recommended option; ask related questions together; never ask for secrets; continue on stated assumptions after a skip. Remove the generic clarification sentence from `McpInvocationRunner` and the envelope prompt and replace it with the same guidance. Status: The guidance lives in the `ask_user` description, so prompts stay within tight context budgets; resumed attempts get `DebatePrompts.ResumeAfterQuestion`.
- [x] Q4.2 Intake phase `intake` (D-9): orchestrator prompt, phase handling in `DebateMethods.AdvanceAsync`, evidence tool phase filter value `intake`, and the transition to round 1 after answers or a decision to proceed. Status: The intake prompt is built in, not part of the prompt library.
- [~] Q4.3 `PromptCatalog`: an opt-in `{answered_questions}` placeholder that expands to the run's answered questions visible to the seat, and the default prompts updated. Prompt validation warns when a custom prompt tells agents to ask questions while `userQuestions` is `off` for the debate being started. Status: `{answered_questions}` is implemented and tested. The prompt validation warning is not implemented.
- [x] Q4.4 Debate quality fixture (`assets/fixtures/debate-quality.json`): add a vague-question case where the expected behavior is an intake question, so `docs/DEBATE_QUALITY.md` reviews cover it. Status: The vague-question case is in assets/fixtures/debate-quality.json for human review; it is a rubric, not an automated check.

Acceptance: prompt tests assert the guidance appears only when the tool is offered; intake tests cover ask, proceed without asking, skip, and the intake limit; a recorded live run (Phase 13) shows an orchestrator asking at intake for a vague question.

Test descriptors: `Runners.QuestionIntake`, additions to the existing prompt checks.

Evidence: `Runners.QuestionIntake` (new debates start in `intake`; the orchestrator's request is limited to `maxIntakeQuestions`; answers reach every seat; a clear question starts without asking; intake counts as an orchestration call) passes. Existing tests that assert exact call sequences set `intake: off`.

## Phase 5: REST API, events and API artifacts

- [x] Q5.1 `QuestionRoutes` registrar, Member access class, `OpenApiRouteMetadata` on every route, tag `Questions`: Status: The path parameter is `{questionId}` because `{requestId}` already names request-history IDs in the generator.
  - `GET /v2.0/api/questions` (filter: `status`, `conversationId`, `runId`, `scope`, `pageNumber`, `pageSize`)
  - `GET /v2.0/api/questions/summary` (open counts for the caller and the tenant)
  - `GET /v2.0/api/questions/{requestId}`
  - `POST /v2.0/api/questions/{requestId}/answer` (answers by question ID or key, idempotency key)
  - `POST /v2.0/api/questions/{requestId}/skip` (optional note)
  - `POST /v2.0/api/questions/{requestId}/defer` (Answer later; parks a held request)
  - `GET /v2.0/api/questions/events` (SSE; tenant-scoped; `question` events and heartbeats)
  - `GET /v2.0/api/conversations/{conversationId}/runs/{runId}/questions`
- [~] Q5.2 Conversation SSE stream carries `question` events; run representations include `awaitingInput`, `openQuestionCount` and `waitingSeconds`. Status: Question changes reach the conversation SSE stream and runs report `waitingSeconds`; the `awaiting_input` state conveys waiting. Separate `awaitingInput` and `openQuestionCount` run fields were not added.
- [x] Q5.3 Error codes: 400 `invalid_answer` (with field path), 404 `not_found` (including other tenants), 409 `question_already_resolved`, 409 `question_pending`, 409 `invalid_state`. Error bodies use the existing shape.
- [x] Q5.4 Debate create and update accept the new policy fields; settings expose `maxLiveQuestionHolds`.
- [x] Q5.5 Remove `GET .../runs/{runId}/clarifications` (route, contract operation, OpenAPI entry) and `clarificationId` and `skipClarification` from `InterjectionRequest`. Interjections that still send them get 400 `invalid_request` naming the questions routes. REST_API.md documents the removal.
- [x] Q5.6 Request history keeps answer bodies within the existing body limit, the same as other writes; document that answers appear in request history for tenant administrators. Status: Documented in docs/QUESTIONS.md.
- [x] Q5.7 Update `assets/api-contract.json`, run `node scripts/generate-api.mjs`, and commit the regenerated `assets/postman/openapi.json`, `assets/postman/arguebot.postman_collection.json`, `REST_API.md` and `assets/transport.json`.
- [ ] Q5.8 API Explorer: confirm the new SSE stream streams in the dashboard Explorer. Status: Not verified.

Acceptance: API tests for every route with a real service on `127.0.0.1` and an isolated home: 401 without a token, 404 across tenants, 409 on double answer from two clients, conformance errors, SSE events in order, contract drift check passing with the new routes.

Test descriptors: `Service.QuestionApi` (or new checks in `Service.SessionCarriers`, matching how `ResourceApiChecks` is wired).

Evidence: `Service.SessionCarriers` runs `QuestionApiChecks` against a real service: 401 on every route without a token, 404 for unknown and other-tenant IDs, 400 filter, page and cursor errors, the removed clarifications route returns 404, and with the simulation a full journey (held question, summary, change feed, run questions, invalid option 400, answer, idempotent repeat, 409 second answer and late skip, debate completes). `node scripts/generate-api.mjs` generated 155 operations and 105 DTO schemas; the contract-drift check passes.

## Phase 6: C# client and dashboard API client

- [~] Q6.1 `ArgueBotClient`: `QuestionsAsync(QuestionFilter)`, `QuestionSummaryAsync`, `QuestionAsync(id)`, `AnswerQuestionAsync(id, QuestionAnswerRequest)`, `SkipQuestionAsync`, `DeferQuestionAsync`, `RunQuestionsAsync(conversationId, runId)`, `QuestionEventsAsync` (SSE, `IAsyncEnumerable`), each with XML docs, cancellation tokens and `ConfigureAwait(false)`. Delete `ClarificationsAsync`. Status: Every method exists; `QuestionEventsAsync` is a cursor read rather than an `IAsyncEnumerable` stream.
- [x] Q6.2 Conversation event parsing in the client handles `question` events and the `awaiting_input` state.
- [x] Q6.3 Record the SDK layout deviation in `docs/REQUIREMENTS.md` (O-7) and add client checks for questions to Test.Shared so every CI host runs them against `127.0.0.1`.
- [x] Q6.4 `dashboard/src/api/client.js`: `questions`, `questionSummary`, `question`, `answerQuestion`, `skipQuestion`, `deferQuestion`, `runQuestions`, `questionEvents`, all with `encodeURIComponent`, normalized pages and errors. Delete `clarifications`.

Acceptance: client tests against a real service for each method and the event stream; dashboard client unit tests; no reference to the removed clarification members remains.

Evidence: the C# methods are exercised by `QuestionApiChecks`, `Terminal.Questions` and the CLI surface script; the dashboard client by the component tests and Playwright journeys.

## Phase 7: Dashboard

Route inventory (DASHBOARD_STYLE_AND_USABILITY.md): add `/dashboard/questions` (inbox) and `/dashboard/questions/:id` (deep link that opens the inbox with that question's modal open) to the inventory before coding. Every answer happens in the question modal (D-16); no page renders answer controls inline.

- [x] Q7.1 `QuestionModal` component built on the shared `Modal` (focus trap, ESC and backdrop close, body scroll lock): title from the request's first header, the asking seat and time, context (sanitized markdown), then each question with its controls per kind (radio group, checkbox group with min and max feedback, text area with counter, Yes and No buttons); **Other** text; recommended badge with text, not color alone; inline validation with a summary at the top; secret warning. Footer actions: **Answer later** and **Skip** on the left, **Submit** (primary) on the right. Closing leaves the request open and never skips. Focus moves to the first control on open and returns to the opener on close; the result is announced through an `aria-live` region. Wide layout at 1280 px, full-screen sheet at 390 px.
- [x] Q7.2 `QuestionModalHost` at the shell level: one instance for the whole dashboard, fed by the tenant question stream. It opens automatically when the user is on a conversation whose run enters `awaiting_input`; on any other page it shows a toast with an **Answer** action instead of interrupting. With several open requests it queues them with "1 of 3" and Previous and Next. If the open request is resolved elsewhere, it switches to a read-only "Answered by X" or "Skipped by X" state with Close. Normal page refresh pauses while the modal is open, but question events still update it.
- [x] Q7.3 Workspace: the transcript shows each request as a compact read-only record (header chips, asking seat, status) with an **Answer** button while open, and who answered with the selected options after resolution. A banner above the composer says which agent is waiting and offers **Answer**. Both open the question modal. Run status shows **Waiting for you** with the waiting time.
- [x] Q7.4 Questions inbox page: table with header, conversation, asking seat, kind chips, status, age (relative time with exact time tooltip), toolbar with "Showing x to y of z", paging controls, page sizes 10/25/50/100, refresh, filters (status, scope mine or tenant, conversation), and row actions Answer, View and View JSON. Clicking an open row opens the question modal; clicking a resolved row opens a read-only view modal. Empty, loading and error states with retry.
- [x] Q7.5 Notifications: sidebar entry under Operate with an open-count badge; a header indicator that opens the next open question's modal; a toast when a new question opens in any conversation, with **Answer** (opens the modal) and **Go to conversation**; the document title gains the open count; optional browser notifications, off by default, enabled per browser from Account (stored in `localStorage` with try/catch, since it is a per-viewer convenience), whose click focuses the dashboard and opens the question modal; live mode indicator with polling fallback. Browser notifications are the only desktop notification channel (O-4). Status: The dashboard polls the change feed every five seconds while the tab is visible and stops when the page is left. An SSE version, and two-second polling, coincided with intermittent `ERR_TOO_MANY_RETRIES` and failed asset loads in the browser suite on the TLS fixture; the root cause in connection handling was not found, so this is tracked as an open risk. The SSE stream remains for API clients, and the polled routes are excluded from request history.
- [x] Q7.6 Home: a **Waiting for you** card listing the newest open questions, each with **Answer** opening the modal. Conversations list: a waiting indicator and filter.
- [x] Q7.7 Debate setup and execution settings (`DebatePolicyEditor`): user questions (`off`, `orchestrator`, `all`), intake, hold, expiry, resume on answer and limits, with help text. Settings page: `maxLiveQuestionHolds` for system administrators.
- [~] Q7.8 Attempt dialog: shows the question request an attempt asked, its parked state, and the resume attempt link. Status: The attempt dialog shows a parked attempt through its `awaiting_input` state; a link from the attempt to its question and resume attempt was not added.
- [x] Q7.9 Remove the clarification block, answer modal and `memory.*` clarification strings from `MemoryPanel.jsx` and `en.json` (migrated requests appear as questions).
- [~] Q7.10 Shared transcript reader (`TranscriptReader.jsx`) always shows question records and answers read-only, like other moderator messages (O-8), with no answer action. Status: Answers are moderator messages, which the shared reader shows; not separately tested.
- [~] Q7.11 All strings in `assets/i18n/en.json` with stable keys, ICU plurals for counts, display labels for kinds and statuses, `Intl.ListFormat` for selected options; en-XA and ar-XB render without clipping; agent-authored text is left untranslated and marked `dir="auto"`. Status: Keys, ICU plurals, kind and status labels, `Intl.ListFormat` and `dir="auto"` are in place; en-XA and ar-XB rendering was not visually checked.

Acceptance: component tests for `QuestionModal` (each kind, validation, Other, min and max, skip, answer later, close without skipping, focus trap and return, 409 handling and the resolved-elsewhere state), `QuestionModalHost` (auto-open on the conversation, toast elsewhere, queue of several requests), the inbox (paging, filters, empty and error states) and notifications; a test that no view renders answer controls outside the modal; Playwright journeys in `dashboard/tests/questions.spec.js` with the provider fixture (agent asks, the modal opens on the workspace, answer, run resumes and completes; on another page a toast appears and its Answer opens the modal; two tabs answer at once and one modal shows the conflict; answer later then answer from the inbox; ESC closes and the request stays open); accessibility spec additions; visual QA at 1280, 768 and 390 px in light and dark themes and in en-XA and ar-XB; `npm run test:ci` and the full Playwright suite pass.

Evidence: `npm run test:ci` 79 tests in 25 files (QuestionModal: every kind, validation summary, Other, bounds, close without skipping, Answer later, skip with note, resolved-elsewhere view, secret warning; QuestionHost: auto-open on the waiting conversation, toast elsewhere, title count, banner with only an Answer button; Questions page: open-first list, toolbar text, filters reset to page 1, read-only resolved view, empty state). `tests/questions.spec.js` passes 3/3 (auto-opening dialog with validation and answer; toast, Esc without skipping, Answer later, inbox, two tabs with the conflict message; three widths in light and dark). Lint and `i18n:check` pass. Screenshots are in `.run/screenshots/v2.3-question-*.png`. Full browser suite: see Phase 13.

## Phase 8: TUI

- [x] Q8.1 `QuestionDialog` (one type per file), a modal dialog like the dashboard's (D-16); the transcript never takes answers inline. It shows header, prompt, context and asking seat; radio list for single choice, checklist for multi-select with min and max feedback, the existing text editor for text, Yes and No for confirm; Other; Submit, Skip and Answer later; works at the smallest supported terminal size and through `TerminalTheme`.
- [x] Q8.2 When the open conversation's run enters `awaiting_input`, show a status-bar indicator and open the dialog automatically (a setting turns auto-open off). Answer later closes it and leaves the indicator.
- [x] Q8.3 A Questions screen from the main menu with the tenant open count in the status bar, listing open questions across conversations and opening the dialog or the conversation. The conversation list marks conversations that are waiting.
- [x] Q8.4 Transcript formatting for question requests and answers (`TranscriptFormatter`), including skipped, expired and cancelled. Status: Answers render as messages labelled with their kind; the ask_user tool record shows the request.
- [x] Q8.5 Replace the "clarifications" run-control item with "Questions", delete `AnswerClarificationAsync`; debate setup (`DebateSetupEditor`, `DebateWizardDialog`) gains the policy fields.
- [x] Q8.6 Optional terminal bell on a new question, off by default. Status: Menu > View and help toggles the bell (off by default) and whether question dialogs open by themselves (on by default), for the session.
- [x] Q8.7 `TerminalHelp` and the keyboard help updated.

Acceptance: Terminal suite journeys against a real service: auto-open and answer each kind, Answer later then answer from the Questions screen, conflict when the web answered first, run resumes; layout probes at the minimum size.

Test descriptors: `Terminal.Questions`, `Terminal.QuestionLayout`.

Evidence: `Terminal.Questions` passes against a real service with the simulation: debate setup defaults and choices, the checklist (bounds messages, Space and Enter), dialogs opening by themselves, answering a single choice, Answer later parking, Menu > Questions waiting for you (1), answering from it and the step resuming. Terminal evidence is saved by `SaveEvidenceAsync` (`questions-*`).

## Phase 9: CLI

- [x] Q9.1 Commands: `questions list [--status S] [--conversation ID] [--run ID] [--all] [--page N]`, `questions show ID`, `questions answer ID` with `--choice KEY=OPTION[,OPTION]`, `--text KEY=TEXT`, `--other KEY=TEXT`, `--confirm KEY=yes|no`, `--skip-question KEY` or `--file PATH|-` (JSON answer body), `questions skip ID [--note TEXT]`, `questions defer ID`, `questions watch` (prints events).
- [x] Q9.2 When a CLI command that follows a run (the existing follow or wait commands) sees `awaiting_input` on an interactive terminal, it prompts for the answer in place; with `--no-input` or without a terminal it prints the question ID and exits with a documented exit code that means "waiting for input", distinct from failure. Status: Implemented as `questions wait CONVERSATION RUN [--no-input]` with exit code 4.
- [x] Q9.3 Remove `debate clarifications` and the `clarificationId` help text. Running the old command prints an unknown-command error naming `questions list` on stderr.
- [x] Q9.4 Help text and `docs/MULTIPLE_AGENTS.md` command list.

Acceptance: `scripts/test-cli-surfaces.mjs` covers every command, JSON output on stdout, errors on stderr, the waiting exit code and the removed command's error; its existing `debate clarifications` check is replaced.

Evidence: `node scripts/test-cli-surfaces.mjs` passed 118 assertions, including `questions list|show|answer`, `questions wait --no-input` exiting 4 and then 0, the refused second answer and the removed `debate clarifications` command.

## Phase 10: Desktop launchers, tray and Docker

The desktop entries today are launchers: the macOS app opens the browser, Windows Start Menu entries open the terminal or browser, Linux ships terminal and browser `.desktop` entries and an AppImage, and a systemd user unit can start the service at login. They gain question support through the dashboard and TUI. The product owner decided against a tray app on November 9, 2026 (O-4), so desktop notifications are browser notifications from the dashboard.

- [ ] Q10.1 Browser notifications from the loopback dashboard work when opened through each browser launcher (macOS, Windows, Linux); document any browser that requires a permission click on `127.0.0.1`. Status: Not done.
- [ ] Q10.2 `arguebot-web` launchers accept an optional `--questions` argument that opens `/dashboard/questions`; add a "Questions" desktop action on Linux and a Start Menu shortcut on Windows if the product owner wants them (record the decision in Q0.1). Status: Not done.
- [ ] Q10.3 `scripts/test-packaged.mjs` checks the new route is served from the packaged dashboard. Status: Not done.
- [~] Q10.4 Docker: nothing new to configure. Confirm SSE question events pass through the container's port mapping, and note `maxLiveQuestionHolds` in `docker/README.md` and `DOCKERHUB_README.md`. Status: docker/README.md and DOCKERHUB_README.md updated; the container check was not run.

Tray companion:

- [-] Q10.5 to Q10.9 Tray companion app (design note, project and packaging, keychain credentials, tests, docs). Dropped by the product owner on November 9, 2026 (O-4): browser notifications only.

Acceptance: launcher checks pass on each built platform, with cross-built artifacts labelled as built, not run; the container serves question routes and events.

Evidence: documentation only; no launcher or container check was run.

## Phase 11: Postman

- [x] Q11.1 Collection: a **Questions** folder with a description and every route, saved examples for each kind of answer, the conflict and the cross-tenant 404, and variables for base URL, token, conversation, run and request IDs. All descriptions follow WRITING_DOCUMENTS.md.
- [x] Q11.2 Ollama mock (`postman/mocks/ollama`): a scenario that returns an `ask_user` tool call, then a final answer once the tool result arrives.
- [x] Q11.3 Simulation (`postman/simulations/runners.sim.yaml`): the same scenario for the runner simulation. Status: The simulation routes to the same mock script.
- [x] Q11.4 Acceptance workflow (`scripts/generate-v2-workflow.mjs`): start a debate whose model asks, poll until `awaiting_input`, list and read the request, answer it, see a second answer fail with 409, see the run resume and complete; a second journey skips the question. Remove the "Inspect clarification requests" request from the generator.
- [x] Q11.5 Regenerate and commit `assets/postman/v2-acceptance.postman_collection.json`.

Acceptance: `node scripts/test-v2-postman.mjs` passes with the new journeys on an isolated home and `127.0.0.1`; collection lint passes.

Evidence: `node scripts/test-v2-postman.mjs` against the simulation: 179 requests and 226 assertions, 0 failures, including the answer and skip journeys and the change feed. The four-agent journey sets `intake: off`.

## Phase 12: Documentation, README and CHANGELOG

- [x] Q12.1 New `docs/QUESTIONS.md`: what questions are, the four kinds, how agents decide to ask, waiting and Answer later, intake, visibility, expiry, limits, answering from each surface (the dashboard and TUI modals, CLI, API, C# client), notifications, the trust and secrets guidance, shared transcripts showing answers, and how 2.3.0 migrated and removed clarifications.
- [x] Q12.2 README: a "Questions for you" section in the interface overview, the CLI commands, and the "About" note for the target version.
- [x] Q12.3 `REST_API.md` (generated) and `MCP_API.md` final review against the implementation.
- [x] Q12.4 Other docs: `docs/MULTIPLE_AGENTS.md` (states, intake, policy fields, CLI), `docs/EXECUTION.md` (hold and park, budgets, capacity, restart recovery, harness timeout and resume flags, live check records), `docs/PROMPTS.md` (guidance and `{answered_questions}`), `docs/TUI.md` (dialog, Questions screen, settings), `docs/DEVELOPMENT.md` (new tests and fixtures), `docs/OPERATIONS.md` (`maxLiveQuestionHolds`, what happens to waiting runs on restart), `docs/IDENTITY.md` (who can answer), `docs/DEBATE_QUALITY.md` (intake case), `docs/REQUIREMENTS.md`, `packaging/README.md` if Phase 10 changes it, `docker/README.md`, `DOCKERHUB_README.md`. Status: packaging/README.md needed no change.
- [x] Q12.5 CHANGELOG `Unreleased: v2.3.0`: the tool, kinds, waiting and resume, intake (on by default for new debates), policy fields, API routes and events, dashboard modals, TUI, CLI, client, schema 35 and migration, the Touchstone 1.0.0 upgrade, and breaking changes: removal of `request_clarification`, the clarifications route, `clarificationId` and `skipClarification`, `clarification_required`, `ClarificationsAsync`, `debate clarifications`, and the `awaiting_clarification` outcome, each with its replacement.
- [x] Q12.6 Search the tree for `clarification` outside `archive/`, CHANGELOG history and the schema 35 migration; no other hit should remain. Search new material for em-dashes and remove them. Status: Remaining hits: the CLI message for the removed command, migration and schema code, the migration test and fixture, docs that describe the removal, and an unrelated summary phrase in a test fixture.

Acceptance: every doc link resolves; the em-dash search finds none in new material; CHANGELOG lists every user-visible change.

Evidence: docs/QUESTIONS.md, MCP_API.md, REST_API.md (generated), README, CHANGELOG, docs/MULTIPLE_AGENTS.md, EXECUTION.md, PROMPTS.md, TUI.md, DEVELOPMENT.md, OPERATIONS.md, IDENTITY.md, DEBATE_QUALITY.md, REQUIREMENTS.md, docker/README.md and DOCKERHUB_README.md. No em-dashes in new material.

## Phase 13: Live checks, simulated user testing and release readiness

- [ ] Q13.1 Live checks on an isolated home and a `127.0.0.1` port, never touching the user's running ArgueBot (stop test instances by exact PID only): Claude Code (held answer and parked resume), Codex (held answer and parked resume), Mux or Gemini CLI over the envelope, and a REST model through Ollama. Record versions, flags and results in `docs/EXECUTION.md`. Status: Not done.
- [ ] Q13.2 A vague-question debate with `intake: ask_if_needed` in which the orchestrator asks, the user answers in the TUI, and the dashboard, CLI and API all show the same resolution. Status: Not done.
- [ ] Q13.3 Simulated user testing session per SIMULATED_USER_TESTING.md on `arguebot-usertest`: inventory and coverage matrix for every new surface, the journeys and probes listed in Requirements compliance, findings in `user-testing/<yyyy-mm-dd>-questions.md` with evidence in the git-ignored `user-testing/evidence/`. The product owner chooses which findings to fix; each fix gets a regression test and a CHANGELOG entry. Status: Not done.
- [~] Q13.4 Full verification: `dotnet build` with no warnings; every Test.Automated suite on SQLite and the PostgreSQL provider job; Xunit and Nunit hosts; `npm run test:ci`, lint and the Playwright suite; `scripts/test-cli-surfaces.mjs`; `scripts/test-v2-postman.mjs`; packaged checks for the built platforms. Status: Local runs listed in the Phase 13 evidence; hosted CI not run.
- [ ] Q13.5 CI: confirm the new descriptors run in every job. Workflow configuration is not evidence that hosted jobs have run; record actual run links when available. Status: Not done.
- [ ] Q13.6 Optional benchmark: answer-to-resume latency through public routes, recorded once, not a CI gate. Status: Not done.
- [ ] Q13.7 Image build and publish only when the product owner asks. Status: Not requested.

Acceptance: all checks above pass or have a recorded, product-owner-approved exception; the simulated user testing report is committed.

Evidence: see the execution record for the final local verification runs.

## Surface coverage checklist

Use this as the final cross-check. A cell is done when the task in it is checked.

| Capability | Service and API | Dashboard | TUI | CLI | C# client | Postman | Docs |
| --- | --- | --- | --- | --- | --- | --- | --- |
| See that an agent is waiting | Q2.10, Q5.2 | Q7.2, Q7.3, Q7.5 | Q8.2 | Q9.2 | Q6.2 | Q11.4 | Q12.1 |
| Answer each kind (modal) | Q5.1 | Q7.1 | Q8.1 | Q9.1 | Q6.1 | Q11.1 | Q12.1 |
| Skip or Answer later | Q5.1 | Q7.1 | Q8.1 | Q9.1 | Q6.1 | Q11.4 | Q12.1 |
| Inbox across conversations | Q5.1 | Q7.4 | Q8.3 | Q9.1 | Q6.1 | Q11.1 | Q12.1 |
| Notifications | Q5.1 (events) | Q7.5 | Q8.6 | Q9.1 (watch) | Q6.1 | n/a | Q12.1 |
| Configure policy | Q5.4 | Q7.7 | Q8.5 | existing debate create body | Q6.1 | Q11.1 | Q12.4 |
| History and provenance | Q1.6 | Q7.3, Q7.8, Q7.10 | Q8.4 | Q9.1 | Q6.1 | Q11.1 | Q12.4 |
| Clarification removal | Q1.2, Q3.2, Q5.5 | Q7.9 | Q8.5 | Q9.3 | Q6.1, Q6.4 | Q11.4 | Q12.5, Q12.6 |

## Risks

- **Nagging.** Agents may ask too often. Per-turn and per-run limits, prompt guidance to ask only when the answer changes the outcome, `orchestrator` mode and intake that gathers questions up front limit this; the debate quality fixture watches for it.
- **Long-held processes.** A held MCP call keeps a CLI process alive. The hold window, `maxLiveQuestionHolds`, capacity release and parking bound the cost.
- **Resume fidelity.** A reconstructed context for a CLI without native resume is not identical to the original session. The resume attempt includes the draft, question and answer, and docs/EXECUTION.md states which harnesses resume natively.
- **Harness timeouts differ by version.** Q3.6 verifies flags against installed versions; a harness that ignores the timeout setting gets `questionHoldSeconds` capped to its limit and parks earlier.
- **Secrets in answers.** Answers are stored in the database and request history. The warning, prompt guidance and the credential-request flag reduce the chance; docs say plainly that answers are stored.
- **Two people answering.** First writer wins with a clear conflict message on every surface.
- **Modal interruptions.** An auto-opening modal can interrupt someone reading a transcript. It auto-opens only on the waiting conversation, elsewhere only a toast appears, and closing it never skips the question.
- **Browser connection flakiness.** With the dashboard following questions over SSE, or polling every two seconds, the full browser suite intermittently hit `ERR_TOO_MANY_RETRIES` and failed asset loads against the TLS fixture server; `main` showed none in one baseline run. Polling every five seconds while visible, and stopping on page exit, gave a clean run, but the root cause in connection handling is not established. Investigate Watson keep-alive and `MaxRequests` behavior before release.
- **Breaking removal.** Scripts and custom prompts that used `request_clarification` or the clarifications route stop working in 2.3.0. CHANGELOG and docs/QUESTIONS.md name each replacement.
- **Touchstone upgrade first.** The 1.0.0 upgrade touches every test host before feature work starts. Q0.7 requires the case count before and after to match, so no test is dropped silently.
- **Prompt injection through question text.** Question text is model output rendered as data, labelled with its seat, never as interface chrome or links that act on their own.

## Execution record

Fill this in as phases land.

| Phase | Commit | Date | Notes |
| --- | --- | --- | --- |
| 0 | fc63f33, c605958 | November 9, 2026 | Branch, version 2.3.0, Docker v2.3.0, requirements review, ADR 0007, Touchstone 1.0.0 |
| 1 to 9 (service, client, CLI, TUI) | 8059cc3 | November 9, 2026 | ask_user, schema 35, engine lifecycle, routes, client, CLI and TUI |
| 1 to 13 (completion) | the commit that adds this record | November 9, 2026 | Intake, placeholder, dashboard modals, tests, Postman, docs |

Final local verification, November 9, 2026, with `ARGUEBOT_CLI_FIXTURE` and `ARGUEBOT_SIMULATION_URL` (the committed mock on 127.0.0.1:4491):

- Release build: 0 warnings, 0 errors.
- Console suite: net10.0 103 cases, 97 passed, 0 failed, 6 skipped (the PostgreSQL cases); net8.0 80 cases, 74 passed, 0 failed, 6 skipped.
- `Test.Xunit3`: net8.0 81 (75 passed, 6 skipped) and net10.0 104 (98 passed, 6 skipped). `Test.Nunit`: net8.0 81 (75 passed, 6 skipped) and net10.0 104 (98 passed, 6 skipped).
- PostgreSQL provider job (`scripts/test-postgresql.mjs`): 6/6.
- `scripts/test-cli-surfaces.mjs`: 118 assertions. `scripts/test-v2-postman.mjs`: 182 requests, 229 assertions, 0 failures.
- Dashboard: `npm run test:ci` 79 tests, lint and `i18n:check` pass; Playwright full suite on a fresh test home: 50 passed, 1 skipped (environment-gated, as on `main`), 0 failed.
- In the first verification pass, `Terminal.Questions` failed once because Answer later returned before the question was recorded as parked; the defer route now records it first. A CLI script run and one NUnit net8.0 run also failed once each and passed on rerun without changes. Hosted CI has not run.
