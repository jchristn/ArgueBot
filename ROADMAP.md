# ArgueBot roadmap

This is the active backlog for the shared C# service, TUI, CLI and React dashboard. It lists remaining work and acceptance evidence, not a request to reimplement features that already ship. Prioritize individual changes with the user before expanding product scope.

Current scope is defined in [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md). The [archived improvement plan](archive/IMPROVEMENTS.md) preserves historical analysis; its checkboxes and status claims are not current commitments. Accounts, pairing, roles, security audit, backup/restore workflows, external monitoring stacks and retired interfaces must not return through this roadmap. Historical database compatibility and upgrade safeguards remain supported.

## Context and generation provenance

- Add explicit selection and inspection of the conversation context used for a generation, with clear inclusion/exclusion reasons and preserved private-directive boundaries.
- Evaluate model-aware token budgets and optional summarization with recorded provenance and visible user controls. Preserve complete original transcripts and never silently truncate them.
- Complete provider usage, completion/exit and failure metadata where the underlying harness or transport exposes it.

Acceptance: tests prove exact selected context, recipient boundaries, budget handling and interrupted retries across CLI and REST participants; both interfaces explain those outcomes.

## Provider and harness compatibility

- Add opt-in live compatibility checks for supported harness and provider versions, including credentials, working directories, cancellation and provider-specific configuration.
- Finish capability-driven configuration, connectivity validation, model discovery where supported, and explicit test-completion actions. Distinguish passive checks from actions that consume provider quota.
- Record exact versions and separate real-provider evidence from fictional process fixtures and local transport simulations.

Acceptance: the supported compatibility matrix links to reproducible results, with missing or untested combinations stated explicitly. Paid/live calls require explicit configuration.

## Cross-interface usability and accessibility

- Finish a coverage matrix for every current screen, command, API operation, setting and documented workflow. Include consensus/final instructions, follow-ups, interrupted work, drafts, simultaneous clients and failure recovery.
- Complete terminal localization and review shared catalogs, pseudo-locales, RTL behavior, keyboard navigation, focus, screen-reader names, long text and narrow layouts.
- Verify complete workflows on native Windows, macOS and Linux environments and supported terminal emulators, beyond headless CI fixtures. Retain responsive light/dark evidence for the dashboard.

Acceptance: each supported user task has reproducible automated coverage or a recorded manual result. Findings identify environment, expected behavior, actual behavior and remaining limitations.

## Operational resilience

- Exercise long-running and concurrent workloads, SQLite contention and PostgreSQL ownership loss, restart during execution, retention boundaries, disk/write failures and dependency outages.
- Review bounded local logs, process captures, request history and event retention. Make necessary retention controls understandable while preserving conversation history.
- Verify startup diagnostics, live versus restart-required settings and source-launcher behavior from clean installations.

Acceptance: failures retain useful history, explain the next action and never silently replay uncertain external work or introduce a second database writer.

## Performance evidence

- Build a reproducible benchmark harness and datasets for startup, conversation/history paging, streaming commits, replay, concurrent runs and memory use on both database providers.
- Establish a measured baseline before tuning. Compare orchestration overhead separately from model latency and keep comparable-environment reports.
- Add reviewed regression thresholds to CI where measurements are sufficiently deterministic.

Acceptance: performance claims link to commands, dataset, commit, environment and baseline results; improvements are remeasured rather than estimated.

## Distribution and release acceptance

- Prepare self-contained artifacts and native installation workflows for the supported Windows, macOS and Linux architectures, including native SQLite and dashboard assets.
- Verify clean install, upgrade, uninstall, service lifecycle, terminal/browser launch and paths containing spaces or non-ASCII text. Preserve user data by default.
- Prepare checksums, license/dependency metadata, package-manager manifests and release preflight. Track external credentials, submissions and availability separately.
- Reconcile requirements applicability, documentation examples and test evidence into a release summary with unresolved risks and explicit skipped coverage.

Acceptance: distinguish built, tested, submitted and publicly available artifacts. Product versions, releases, package publication and external submissions require explicit user authorization.

## Keeping this roadmap current

Remove or rewrite an item when its outcome is verified, linking evidence in the relevant change or development documentation. Update [CHANGELOG.md](CHANGELOG.md) under Unreleased and keep [development commands](docs/DEVELOPMENT.md), [operations](docs/OPERATIONS.md) and API artifacts consistent. Do not mark a broad acceptance gate complete because one implementation slice or CI run passed.
