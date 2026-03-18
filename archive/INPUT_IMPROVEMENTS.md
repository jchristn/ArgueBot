# Input Improvements Plan

## Agreed Direction

Replace the current Ink/React multiline input implementation with a lightweight imperative editor built on raw `stdin`, ANSI rendering, and a small byte-oriented parser. Keep `readline` for ordinary single-line prompts.

## Goals

- Match the inline terminal feel of Claude Code and Codex as closely as possible.
- Let users paste multi-line content confidently without accidental submission.
- Keep the implementation lightweight and easy to reason about.
- Preserve exact user input; do not trim or normalize text in the input layer.

## Architecture

Implement multiline input as a pipeline:

`process.stdin` -> incremental byte decoder -> semantic input events -> editor state -> ANSI renderer

### 1. Byte Decoder

Build a small state machine that consumes raw bytes and emits semantic events.

Responsibilities:

- Decode ordinary characters safely, including chunk carryover for split UTF-8 sequences.
- Distinguish standalone `Esc` from CSI / escape sequences with a short timeout.
- Parse navigation keys such as arrows, Home, End, Delete, and Backspace.
- Support bracketed paste start/end markers: `\x1b[200~` and `\x1b[201~`.
- Treat chunk-based paste detection only as a guarded fallback when bracketed paste is unavailable.

Important constraint:

- Do not treat `stdin` chunk boundaries as semantic input events. They are transport details only.

### 2. Editor State

Use a single atomic editor state object instead of split state.

Suggested state:

- `lines: string[]`
- `cursorLine: number`
- `cursorCol: number`
- `isPasting: boolean`
- `escPending: boolean`

Responsibilities:

- Insert characters and newlines.
- Move the cursor predictably.
- Support backspace and forward delete correctly.
- Preserve pasted content verbatim.

### 3. Renderer

Render directly with ANSI escape codes.

Requirements:

- Prompt and user input appear on the same line.
- Use the real terminal cursor, not a fake `|` character.
- Clear and redraw efficiently after each edit.
- Track wrapped lines well enough to keep cursor placement stable.
- Show concise multiline instructions when input starts so the submit contract is discoverable.

## Input Semantics

### Multiline Mode

- `Enter`: insert newline
- `Esc` then `Enter`: submit buffer
- `Ctrl+C`: cancel/exit
- Show a visible hint when multiline mode starts, such as:
  `Enter = newline, Esc then Enter = submit, Ctrl+C = cancel`
- The hint may be dimmed or reduced after the user begins typing, but it must be present at entry.

Rationale:

- `Shift+Enter` and `Ctrl+Enter` are not reliable across terminals.
- `double-Enter` is content-sensitive and can conflict with real pasted or authored text.
- `Esc` then `Enter` is deterministic and does not steal document structure from the buffer.

### Paste Handling

Primary mechanism:

- Enable bracketed paste mode with `\x1b[?2004h`
- Parse `\x1b[200~ ... \x1b[201~`

Fallback:

- If bracketed paste is not observed/available, use a guarded heuristic inside the decoder for multi-byte chunks containing newlines.
- The fallback must never become the primary contract.

## Normalization Policy

Remove trimming from the input layer entirely.

Required changes:

- Do not use `.trim()` or equivalent in multiline input.
- Do not use `.trim()` on the single-line `readline` path in `src/ui.ts`.
- Let higher-level callers decide whether any prompt-specific normalization is appropriate.

## Scope of Rewrite

### Replace

- The current Ink/React-based multiline editor in `src/input.tsx`

### Keep

- `readline` for basic single-line prompts in `src/ui.ts`

### Remove if no longer needed

- `ink`
- `react`
- `@types/react`

## Implementation Steps

1. Create a new non-React multiline input module, likely `src/input.ts`.
2. Implement the incremental byte decoder with escape-sequence parsing and bracketed paste support.
3. Implement the editor state model and editing operations.
4. Implement ANSI rendering with inline prompt and real cursor.
5. Update `src/ui.ts` to route multiline prompts to the new module and keep single-line prompts on `readline`.
6. Remove trimming from input collection paths.
7. Delete the old Ink/React input implementation and remove unused dependencies.

## Verification Checklist

- Prompt appears inline with typed text.
- Single-line typing feels normal.
- Multi-line typing preserves exact whitespace.
- Pasting multi-line content does not submit accidentally.
- `Esc` then `Enter` submits reliably.
- Arrow keys, Home, End, Backspace, and Delete work correctly.
- Forward delete deletes forward, not backward.
- Bracketed paste mode is enabled on entry and disabled on exit.
- Terminal state is restored cleanly after submit, cancel, or error.

## Notes

- The parser should be incremental and byte-oriented, not regex-per-chunk.
- The renderer should stay simple, but correctness matters more than cleverness.
- If wide-character handling becomes a problem, add width accounting separately rather than complicating the initial rewrite.
