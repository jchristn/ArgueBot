# Model-guided debates with explicit ratification

Status: accepted design for implementation; verification remains in progress.

New debates use a versioned protocol with an explicit orchestrator profile, stable seat snapshots, context epochs and a proposal-bound vote from every active seat. The service owns legal transitions and finalization. Model output proposes questions, summaries and final text; it cannot decide that other participants agreed.

Historical runs retain their original protocol and outcome. The new API surface uses `/v2.0/api`; existing `/v1.0/api` routes remain compatible. New clients select protocol 2 explicitly, and a missing orchestrator fails before invocation. A legacy run is never silently converted by selecting its first speaker as orchestrator.

The scheduler starts sequentially. Roster edits apply only at a safe invocation boundary and preserve historical seat snapshots and dissent. Moderator context is durable immediately, with `interrupt_now` or `next_turn` recorded for each submission. Context and roster changes invalidate pending ratification. Uncertain interrupted model or tool work is resumed explicitly.

New persistence uses structured tables for protocol state, source membership, proposals, votes and interjections, with atomic attempt finalization. Original messages remain authoritative; summary activation never deletes the transcript. Private directive selection and retrieval stay scoped to the receiving invocation.

Recipient-only directives are scoped to their seat. They do not advance the shared context epoch or reset round progress, and Interrupt now cancels only the recipient's own active turn. Before a run finalizes, a recipient that has not yet received a pending directive votes again on the same proposal. Its earlier vote moves to `debate_agreement_revisions`, and only its newest vote counts. This keeps the shared prompt and schedule free of directive counts while still letting the directive affect the outcome.

Invalid structured output, including a summary that fails validation, gets one repair. The failed reply stays as a failed attempt, and the next attempt for the same step (marked `repair` in `debate_attempts`) carries a correction request. A failed repair pauses the run; a moderator resume is an ordinary attempt. Moderator proposal edits insert a new proposal and deactivate the old one in the same transaction as the run checkpoint, guarded by the run revision. A ratification attempt records the proposal it was asked about, so a vote that completes after an edit is ignored rather than counted against the replacement or treated as invalid output.
