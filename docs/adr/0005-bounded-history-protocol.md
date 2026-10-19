# Bounded history and protocol 2

The replacement HTTP attachment protocol advances to 2. This is an internal compatibility boundary; the product version remains unchanged, and this change needs no database migration. Native local clients refuse mismatched protocols before using cached credentials. Upgrade client and service binaries together. Stop an older service with its matching client or authenticated shutdown route before replacing its binaries.

GET conversation messages now returns `MessagePage`, replacing the unbounded array. Each page has exact totals, bounded Unicode previews, full UTF-8 content sizes and truncation flags. GET an individual message for its complete text. Previews never replace stored content or model context, and exports must hydrate them before treating their content as complete.

An omitted `throughSequence` captures current membership. Reuse it to traverse a stable set while new messages arrive. `latestSequence` identifies newer membership; `tail` selects the last page. The boundary does not freeze streaming content within an existing message. Completed message text is immutable. Cross-conversation and cross-tenant access follows the same ownership check as other history operations.

Preview length is configured in Unicode code points, default 4096 and range 128 through 16384. Page size is one through one hundred. This bounds background interface payloads without silently truncating an export. The web has explicit full-message reading and older-page navigation; native TUI polling reads the latest preview page, while Ctrl+L opens paged/full history. Ctrl+H is avoided because legacy terminal input interprets it as Backspace. The console supports cancellable full-message enumeration, streaming JSONL and streamed Markdown export.

The legacy TypeScript server and its npm entry points remain separate and runnable during migration. No transparent compatibility with that server is claimed. These changes do not complete the remaining replay-retention, authorization, terminal localization, export-job and cutover gates.
