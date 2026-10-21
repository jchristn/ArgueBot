# Repository instructions

Read ROADMAP.md and docs/REQUIREMENTS.md before changing the runtime. The normative requirements are in ~/Code/Agents/requirements; their reviewed hashes are in docs/requirements-manifest.json.

Do not change product versions or prerelease labels without an explicit user request. Use Unreleased in CHANGELOG.md. Never write secrets or real user homes into fixtures. Test with isolated application homes and 127.0.0.1 addresses.

C# uses block namespaces, inside-namespace alphabetized usings, explicit types, one type per file, _PascalCase private fields, no tuples or partial classes, XML documentation on public APIs, cancellation tokens, ConfigureAwait(false) in libraries/server, and classic using blocks. Nullable, warnings-as-errors and deterministic builds are enabled. Libraries and shared tests do not write to the console. No em-dashes in new material.

The C# service, TUI and React dashboard are the supported implementation. The service is the sole database writer and execution owner. Interfaces use its open-workspace API contract. Update code, meaningful tests, API artifacts and documentation together. Do not mark plan items complete without evidence.
