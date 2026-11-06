# Generation interruption investigation

Evidence date: October 21, 2026. This investigation concerns ArgueBot running a Claude Code/Codex debate about Armada. The Armada code examination is a separate deliverable in that repository. No external telemetry stack was added.

## Observed failure

The local service associated `msg_mvilesad_jubdcfxrpeh` with attempt `att_mvilesad_WwZhv84p3wO`. Its participant snapshot had `TimeoutSeconds=300`. The attempt ran for 300073 milliseconds; its local diagnostic recorded cancellation and process exit 137. It had retained partial assistant text and a nested auditor result arrived immediately before the cutoff. The run paused as `interrupted`, while the attempt had an empty error code. This evidence establishes a generation deadline, rather than a dashboard disconnect or a malformed assistant payload, for this occurrence.

The UI initially rendered only a partial/interrupted notice for interrupted messages. Its failure explanation component was restricted to failed messages. The attempts API therefore lacked a useful cause, and the workspace did not provide an inspection action beside that interrupted turn. HTTP request success counts could remain green because run acceptance and history reads succeeded while the background generation failed.

## Successful Claude completion rejected during the reproduction

The first monitored attempt ran for 839093 milliseconds and wrote the Armada report, then failed with `InvalidDataException`. Its bounded capture retained a successful Claude `result` event with `result_index: 0`. That event repeated an earlier assistant response after later background-auditor wakeups and the final report-writing response had already streamed. The parser incorrectly compared it with the latest assistant message, reported a conflicting snapshot and killed the process. The retained exit 137 was caused by the adapter's fail-fast termination; it did not establish an upstream model failure.

The parser now treats indexed result records as distinct stable result identities. An indexed result matching an earlier assistant message emits no duplicate text. Previously unseen indexed text is retained. Repeated identical indexed results are ignored, conflicting replacements for the same result index still fail, and legacy unindexed snapshot validation remains strict. Sanitized subprocess fixtures cover these cases through a durable engine completion. The original raw capture stays in the ignored investigation home.

## Changes and remaining gaps

| Item | Evidence or change | Status |
| --- | --- | --- |
| Indexed Claude result rejected after successful work | A successful indexed result can repeat an older assistant message after background wakeups. It no longer gets compared with the latest message. Conflicting replacements for the same index still fail. | Implemented; sanitized subprocess replay and durable engine regression |
| Deadline misclassified as interruption | CLI and REST adapters distinguish their own deadline cancellation from caller cancellation and report `generation_timeout`. The failed partial answer, attempt and paused run preserve that cause. | Implemented; fictional process, real HTTP simulation and browser coverage |
| Explicit cancellation has no cause | New orderly interrupt, stop and service-shutdown paths record distinct safe attempt codes. Historical blank causes retain an explicit unknown-cause explanation. | Implemented; explicit interrupt, stop and orderly shutdown regression coverage |
| Interrupted card hides details | Dashboard interrupted cards and TUI interrupted transcripts use attempt recovery guidance and keep the inspection path available. | Implemented; component and shared terminal coverage |
| Inspecting an older failure opens the newest run | Failure-card inspection now passes that message's run ID into the attempt inspector. The browser regression creates a newer run and verifies that the older card still opens its own attempts. | Implemented; browser regression |
| Hard deadline is invisible during execution | Dashboard activity shows elapsed browser-clock time and the frozen timeout. Web and terminal attempt inspectors show the participant snapshot's timeout. Resume keeps that snapshot. | Implemented; live browser observation and browser regression |
| Background work lacks useful log correlation | Generation lifecycle logs include attempt/message/run IDs, the start deadline and terminal elapsed time. Worker-level errors now emit safe type/code records. | Implemented; logs contain no raw prompt or protocol output |
| CLI tools and nested auditors are invisible | The Claude adapter deliberately excludes tool payloads and nested-agent messages from assistant text. Their progress is absent from the built-in activity display. | Remaining: introduce a dedicated bounded activity contract with tool names, lifecycle, timestamps and parent attempt IDs; exclude arguments, results and private reasoning by default |
| Activity text can be stale | The card now says answer text has been received and shows how long this view has observed no answer-text change. The timer disclaims heartbeat semantics. | Implemented browser observation; remaining: persist protocol/tool activity timestamps so silence can be measured across reconnects and clients |
| Successful harness terminal metadata is absent | Failure captures retain bounded exit/output evidence; successful attempts do not expose exact harness version, finish reason, usage or exit metadata. | Remaining: normalize supported terminal metadata without leaking payloads |
| Per-turn deadline includes all tool work | A long repository audit legitimately takes longer than the default five minutes. Live output does not reset the hard deadline. | Documented; monitored rerun uses explicit 3600-second participant snapshots |
| Request activity conflates acceptance with execution | A `202` response means accepted work, not completed generation. SSE heartbeats mean transport connectivity. | Documented; retain separate run/attempt outcomes and add built-in outcome summaries in future |
| Rebuilt UI and stale service can disagree | The original service process predated the failure-guidance build and omitted `errorMessage`. Reloading the browser did not replace its loaded assemblies. After confirming no active runs, the default service was shut down and started with the rebuilt runtime; historical attempt guidance is now returned. | Operationally corrected; Settings restart reloads configuration, not assemblies |
| No automatic retry of uncertain external work | Repeating an audit may duplicate file writes, model charges or other effects. | Changed in 2.2.0 at the user's request: interrupted or uncertain attempts after a restart still need an explicit resume, but a debate repeats a step automatically after an invalid structured reply or a transient failure, up to `maxStepRetries` (default 3, 0 turns it off). See [automatic retries](MULTIPLE_AGENTS.md#automatic-retries) |

## Verification and monitoring

The reproduction uses an isolated home under `.run/handoff-monitor`, a loopback service on port 4517, Claude Code followed by Codex, 25 rounds (at most 50 individual turns) and 3600 seconds per turn. The original Armada question is submitted unchanged. Initial run ID: `run_mvin7znu_ohXt4vQnonL`; conversation ID: `cnv_mvin7znl_J5bbHffq2tv`.

The initial run preserved its failed attempt and the report it wrote. A factual public note retains its output as review context; no attempt was rewritten as successful. After loading the parser fix, continuation `run_mvintq9i_1q9zFtnrizG` starts with Codex reviewing the existing report, followed by Claude. It permits 24 rounds, so the first attempt plus the continuation remain below 50 individual attempts. The original question is unchanged.

A local monitor samples run checkpoints, all attempts and message sizes every five seconds into ignored JSONL evidence. A second monitor reads only tool names, call identifiers, lifecycle and error flags from the correlated Claude harness transcript and its nested auditor transcripts. It excludes prompts, tool arguments, raw results and reasoning. These harness-side observations are investigation evidence, not a shipped product monitoring feature.

The checks include a real fixture CLI deadline through the service engine, a separate explicit interrupt, a partial hanging Ollama HTTP stream through PolyPrompt against the committed Postman simulation, safe historical fallback guidance, dashboard component checks and a browser test that reloads the failed turn and inspects its frozen timeout. Live paid-provider evidence and fictional/simulated regression evidence are reported separately. PostgreSQL checks require a separately configured disposable database.

Regression evidence: the final shared console run passed 58 of 60 descriptors, with only the two unconfigured PostgreSQL descriptors skipped. All 16 runner checks subsequently passed with CLI fixtures and the Postman simulation, including explicit interrupt, stop and shutdown causes. All 24 browser checks passed. Dashboard component coverage passed 25 checks; lint and localization validation passed. These results do not establish PostgreSQL live compatibility.

## Final live outcome

Continuation `run_mvintq9i_1q9zFtnrizG` reached `completed / consensus` on October 21, 2026 at approximately 22:08:57 UTC. All three continuation attempts completed successfully: Codex review in 122960 ms, Claude reconciliation in 105397 ms and Codex acceptance in 36429 ms. Together with the initial failed audit, this investigation ran four attempts, below the requested maximum of 50. There were no automatic retries or rewritten attempt outcomes.

The final Armada report contains 68 finding identifiers and 14 scored review challenges. L5/H1 share a table row, giving 81 scored rows in total. Every simplicity/value score is between one and five, every total equals their sum, and finding identifiers are unique. The supplied password is absent. Both agents agreed on the qualified recommendations; live counts were attributed to Claude's inspection, and Codex explicitly did not independently reproduce every observation.

The ignored evidence directory contains both run configurations, per-five-second observations, tool-only harness checkpoints, the full transcript, test outputs and `result-summary.json` with the report SHA-256. The two original failure captures remain associated with their actual failed/interrupted attempts. Built-in product logs do not expose the harness-side tool monitor as a product feature.

Live harness versions were Claude Code 2.1.306 and codex-cli 0.163.0; development verification used .NET SDK 10.0.401 and Node v24.21.0. Real provider evidence covers this workload and these installed versions, not every supported model or harness. The default service was stopped and started with the fixes only after checking that it had no active runs. The isolated completed conversation remains available on loopback port 4517.
