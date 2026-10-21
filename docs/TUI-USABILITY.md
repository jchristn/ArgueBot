# Terminal usability review

This review covers the C# TUI. It shares the open-workspace service contract and persistent execution owner with the React dashboard.

## Reference applications

The local reference implementations informed the interaction rules, rather than supplying a visual theme to copy:

| Reference source | Useful convention | ArgueBot application |
| --- | --- | --- |
| Armada, `src/Armada.Tui/Widgets/KeyHints.cs` | Hints follow the deepest focused control; typing suppresses unrelated actions | Conversation list owns its letter shortcuts and supplies its own footer hints |
| Armor, `src/Armor.Tui/TuiController.cs` and `Widgets/FooterHints.cs` | Common entity operations use visible mnemonic keys, including d Delete | d/Delete operates on the highlighted conversation |
| S3Drive, `src/S3Drive.Tui/TuiController.cs` | Actions belong to a pane and act on that pane's selection | Local shortcuts resolve the highlighted row before opening a workflow |
| Mux, `src/Mux.Cli/App/MuxTuiApp.cs`, `MenuBarBuilder.cs` and `MuxCommandCatalog.cs` | Composer and modal interactions have visible keys; menus provide discoverable command access | Text entry retains ordinary letters; F1/F2 remain available; the menu teaches global shortcuts |

## Findings and adaptations

| Surface | Finding | Result |
| --- | --- | --- |
| Startup | Toolbar focus makes the primary conversation workflow indirect | Initial focus is the conversation list |
| Conversations | Delete and other frequent actions require menu traversal | d/Delete, b debate, r controls, p pause/resume, s stop, f follow-up, l history, g attempts and a note |
| Selection | Highlighted row and open history can refer to different conversations | Local actions explicitly resolve the highlighted row; confirmations name the target |
| Discovery | Footer prioritizes generic menu access over useful local actions | Local action hints appear on list focus, with Delete first; F1 remains reserved |
| Navigation | Returning to a pane requires repeated Tab presses | Alt+1 conversations, Alt+2 history, Alt+3 note; Escape returns to conversations |
| History | Focus lacks the indicator provided for the list and composer | History becomes a keyboard focus stop with arrow/page scrolling, Home/End, Enter for full history and an accent chevron retaining its title |
| Composer | Global follow-up on Ctrl+A conflicts with standard input expectations | Ctrl+Y opens follow-up; Ctrl+A remains available to the input control |
| Main menu | Actions do not teach their existing global shortcuts | Labels include shortcuts; the menu remains the exhaustive fallback |
| Deletion | Permanent removal must not happen from a stray letter | d/Delete defaults to Cancel; an unfinished run offers a confirmed combined stop, wait and automatic delete flow |
| Setup and agents | Multi-step setup and long forms need preserved drafts and reachable actions | Retain guided review, presets, scrolling forms, Ctrl+S and existing validation/error handling |
| Dialogs | Global keys must not escape a modal or replace a draft | Modal ownership remains intact; F1 context help and innermost Escape behavior are retained |
| Narrow terminals | Not every hint fits on one footer line | Prioritize local actions, retain F1, and document the complete key map; keep resize protection below 60 by 20 |
| Administration | Rare, privileged operations should remain discoverable without consuming local letters | Retain Administration menu and existing global shortcuts; service permissions remain authoritative |

Letters apply only while the conversation list has focus. Up/Down still moves the cursor and Enter opens the row; this preserves deliberate reading while ensuring an action targets the visible selection. Note entry and modal fields do not interpret those letters as commands. Menus act on the open conversation, as their labels specify.

## Verification and limits

Run `dotnet run --project src/Test.Automated -c Release -f net10.0 -- --suite Terminal`. The 15 terminal descriptors drive the actual TUIKit shell, including 60/80/120-column shortcut journeys, highlighted-versus-open deletion, default cancellation, confirmed Delete, note typing, pane navigation and focus indicators. A real loopback service with an isolated home and hanging CLI fixture verifies active/paused stop cancellation, s/F8 stopping, deletion after completion and combined stop/delete without another key sequence. Existing journeys cover service refusal during deletion, mouse setup from an empty service, draft preservation, stale responses, pending submissions, keyboard/mouse scrolling, forms, themes and resize handling.

This is local automated interaction evidence. It does not close the complete P5/P6 parity, localization or P11 acceptance gates, and does not establish manual behavior on every terminal emulator. Full release acceptance remains tracked in IMPROVEMENTS.md.

The help contents now include eight explanatory guides, with separate post-consensus and troubleshooting topics. A headless journey opens every actual topic at 60, 80 and 120 columns, reaches its final instructions and returns to the contents. Menu-wrapping checks use those same titles.
