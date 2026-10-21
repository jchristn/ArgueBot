# Execution and recovery

The service owns runs and external invocations after clients disconnect. A run freezes its original question and participant configurations. Renaming a conversation or editing a reusable profile does not rewrite an accepted run's question or historical runner settings. Runs created before schema 9 receive the best available conversation title during migration; an earlier title changed before that upgrade cannot be reconstructed. Historical invocations are not fabricated into attempt records.

Schema 9 records each generation attempt separately from its answer message. Acceptance commits a pending attempt, new streaming answer, exact ordered source message identifiers, system-prompt version and SHA-256 digest, and the agent-turn checkpoint together. A separate streaming checkpoint precedes the actual CLI/REST call. Neither checkpoint proves whether an external operation ran if the process crashes at that boundary.

Successful completion atomically finalizes the answer and attempt, advances the speaker/run checkpoint, and consumes directives delivered to the intended participant. A failed or interrupted attempt keeps those directives eligible for an explicit retry. Other participants never receive their text as model context. Partial answers remain in history and are excluded from subsequent model context. An exact terminal checkpoint retry does not advance a speaker twice.

Startup marks pending/streaming attempts interrupted and pauses uncertain runs. It never replays a harness or completion request automatically. Resume is explicit and can repeat external work or incur provider costs. If finalization could not reach storage, restart and reconcile before retrying an unfinished attempt. The service exposes this condition as `uncertain_attempt`.

A pause requested during the final answer cannot strand an exhausted run: once that answer completes, the run records its completed outcome. Resuming an older exhausted checkpoint finishes it without another invocation. An explicit stop still takes precedence.

Inspect attempts through the workspace's Generation attempts control, Ctrl+G in the TUI, or:

```sh
dotnet run --project src/ArgueBot.Cli -c Release -- debate attempts CONVERSATION RUN
```

The web and terminal inspectors offer historical runs, paging, refresh, prompt provenance and ordered source identifiers. The web also shows source previews from the loaded transcript. The CLI returns the complete typed page as JSON. Elapsed milliseconds cover generation and durable stream flushing using monotonic time; zero means unavailable or below clock resolution. These are diagnostic values, not benchmark claims.

Context budgets count the exact UTF-16 characters in the system prompt plus selected message content and separately bound message count. Defaults are 262144 characters and 1000 messages. `ContextMaxCharacters` accepts 1024 through 16777216; `ContextMaxMessages` accepts one through 10000. Save them in Settings or the service configuration and restart to apply. Over-budget work pauses with `context_budget_exceeded` before creating an attempt or invoking a model. No transcript text is silently dropped. Preparation reads bounded recipient-filtered metadata first, limits eligible row count and UTF-8 payload size, then checks exact UTF-16 content before accepting the attempt. Completed source content is immutable; consuming a directive changes only its lifecycle state through successful attempt completion.

Current context selection uses completed conversation messages and eligible unconsumed recipient directives. Character limits are not model token estimates and do not guarantee a provider accepts the prompt. Explicit user-selected context, model-specific token accounting, cancellable summarization with provenance, provider-reported usage and detailed harness exit metadata remain open plan gates.

History reads use protocol 2. Message pages contain bounded previews and exact membership metadata; full text is available by message ID. The web freezes older-page membership while newer messages arrive and offers an explicit return to the live tail. Ctrl+L opens paged terminal history and a read-only full-message reader. The CLI's `conversations show ID --jsonl` streams complete records and Markdown export enumerates complete messages without collecting the transcript. Browser downloads also hydrate previews and include messages beyond the visible page. The captured membership boundary does not freeze in-progress answer text. See [the compatibility decision](adr/0005-bounded-history-protocol.md).

Replay events are a temporary delivery log, separate from permanent transcript history. Maintenance retains a contiguous suffix and always preserves its latest sequence anchor. Defaults retain up to 10000 events per conversation for 30 days, with maintenance every 60 seconds in batches of 100 conversations. Expired nonzero cursors return `409 event_history_gap`; future cursors return `400`. Clients must load a fresh conversation head before refreshing snapshots, then subscribe after that captured head. The dashboard performs this recovery automatically. Native `WatchEventsAsync` exposes the typed failure so callers can refresh their own snapshots. Both readers validate strict UTF-8, conversation identity, sequence continuity and bounded framing; duplicate events are ignored.

Stream callbacks larger than the configured batch size are split before persistence, retaining complete Unicode surrogate pairs. Invalid trailing runner text finalizes the attempt with `invalid_runner_text` while preserving the valid committed prefix. Storage failures can still leave uncertain attempts, which follow the explicit recovery rules above.

Conversation and profile storage uses the fixed workspace scope. Historical ownership columns are internal compatibility keys, not product accounts.

Accepted execution is service-owned and does not depend on a client session or active account. The open workspace bypasses legacy identity checks. Stop and interrupt cancel external work; disconnecting a UI does not. Historical authorization-related outcomes remain readable but do not define the current product workflow.

## Agent program configuration

In both the terminal Agents form and the dashboard Participants form, Executable means the program name or full path only. For example, use `claude`, with flags in separate argument rows. A path containing spaces is one value and does not need shell quotes. Each argument row supplies one flag or value; `--output-format` and `stream-json` use two rows. No rows uses the built-in defaults; any custom rows replace the entire default argument list.

Both forms explain this visibly and reject common entries such as `claude --verbose` in the executable field. Launch failures recorded as `Win32Exception` now have a plain-language explanation in the attempt inspector; the dashboard also explains that outcome in the workspace. No raw process error text is exposed. Profile edits affect future run snapshots, so start a new debate after correcting a profile rather than resuming a run with the old settings.

## CLI message streams

Codex can emit multiple `agent_message` items in one turn, including progress and a final answer. The process adapter tracks each item's text by its stable item ID. Updates emit only their new suffix, completed duplicates emit nothing, and distinct messages are separated by a blank line. A different message is not required to repeat the previous message's text. A conflicting replacement of the same item's already emitted prefix remains a failure.

The `Runners/CodexMessages` shared descriptor drives a real fixture subprocess through updates, distinct messages, duplicate completions and conflicting replacements. Run it with the built Test.Fixtures.dll supplied through `ARGUEBOT_CLI_FIXTURE`. The separate live local Claude/Codex smoke debate completed both attempts after this fix. The originally failed debate was then explicitly resumed: Codex retried successfully, Claude completed the next turn and the run reached consensus in round two. The failed historical attempt remains recorded. Provider REST simulations are separate checks and were not rerun for this subprocess parser change.

Named harness setup uses the shared `assets/harness-defaults.json` catalog in the web, TUI, service and execution fallback. Selecting Claude Code, Codex, Gemini CLI or Mux fills executable and arguments. Claude Code includes `--dangerously-skip-permissions` and Codex includes `--yolo` to run debate turns unattended. Generic CLI remains manually configured. The service fills omitted values for API/CLI create and update too, and returns the persisted defaults. Explicit custom arguments are preserved and replace the full list. Existing run snapshots are not rewritten.

## REST local tools

REST participants use native tool calling through PolyPrompt for Ollama, OpenAI-compatible, Anthropic and Gemini models. The service advertises `read_file`, `write_file`, `list_directory` and `run_process`, executes requested operations and sends results back to the model until it answers. The model must support native tools. Working directory applies to both CLI and REST profiles; empty uses the service directory. Relative paths resolve there. This is a working directory, not a filesystem sandbox: operations have the service account's permissions. Container deployments operate inside the container and its explicit mounts.

The existing `allowCli` setting now controls all local execution: CLI harnesses and REST tools. When false, REST chat continues with no local tools advertised. No additional permission prompt is required during a turn. File access uses explicit paths; processes use an executable plus literal argument array with no implicit shell parsing.

Limits are 32 calls and 16 continuation rounds per turn, 128 KiB of streamed tool argument data per response, 64 KiB per file read/write or process output stream, 200 directory entries, 256 KiB per serialized result, and 30 seconds per process within the participant's overall turn timeout. Tool errors are returned to the model so it can correct a request. Incomplete provider responses never execute their proposed calls. No provider request or local tool is automatically retried. Streaming continuation preserves provider tool IDs and Gemini thought signatures/native continuation state.

Before a tool operation, the engine commits a `tool` message with the call identifier, arguments and started status. Another tool message records its completed, failed or interrupted result. Normal conversation events make both visible in web and TUI history, including after reload. Tool details remain separate from the model's answer; the request's initial prompt digest includes the tool-use instructions. Call/result continuation messages are not separate generation attempts. The transcript contains local tool inputs and outputs, just as it contains model answers.

Stop cancels the turn and terminates its running local process tree. Pause waits for the current turn. Cancellation, service shutdown or a crash may leave file changes or external effects, even if no final result was recorded. Inspect a pending or interrupted tool before explicitly retrying the turn; retry can repeat earlier operations. A started entry is evidence of acceptance, not proof of completion.
