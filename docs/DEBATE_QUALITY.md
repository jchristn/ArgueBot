# Reviewing debate quality

The [synthetic corpus](../assets/fixtures/debate-quality.json) covers contradictory evidence, a late correction, changed assumptions and persistent minority dissent. It is separate from deterministic transport fixtures: those fixtures prove execution, persistence and boundaries, but their canned responses cannot establish model judgment or summary fidelity.

Use an isolated workspace with the intended model profiles. Explicitly running these cases may consume provider quota. Record the exact harness versions, provider model IDs, endpoint protocol, model-check observations, generation settings, run policy, database provider and source revision. Keep credential values out of the report.

For each case:

1. Create a conversation with the supplied question and add each context entry as a separate original note. Assign the minority stance to a participant where the case requests it.
2. Select the intended panel and a separate orchestrator. Set a small enough context budget to exercise repeated compaction while leaving enough room for a complete source and the tool schemas. Repeat the case with a larger budget for comparison.
3. Submit each listed interjection at the stated phase, preserving its delivery mode and pin choice. Observe the durable receipt and whether participants received the original or a source-linked summary.
4. Inspect the seven summary sections, parent summaries, original source hashes, orchestration questions, proposals and each explicit vote. Compare them with every required observation and unacceptable claim in the case.
5. Export the original transcript and record a pass, fail or unresolved judgment for each observation, with message/summary/proposal IDs supporting the judgment. Record omissions and uncertainty as failures or unresolved findings rather than silently correcting the model's output in the report.

Evaluate questions for relevance to the actual disagreement, neutrality, requests for missing evidence and fair participation. Evaluate summaries for attribution, corrections, constraints, minority positions and uncertainty. A structurally valid summary can still omit or distort a fact; a content hash establishes which original was cited, not that the summary understood it.

No live-provider quality judgment is implied by the repository's automated test results. The original [implementation plan](../MULTIPLE_AGENTS_PLAN.md#17-implementation-evidence-and-active-execution) records observed verification and unverified provider/platform combinations. Record live case results there with links to sanitized evidence; do not replace that plan with a second completion checklist.
