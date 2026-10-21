# Using the C# terminal interface

The C# TUI and web dashboard are the supported conversation interfaces. Both use the same persistent service.

Build and launch from the repository root with `./go-tui.sh` on macOS/Linux or `go-tui.bat` on Windows. Both build the C# solution first and forward TUI arguments, for example `./go-tui.sh --home .run/home`. A manual equivalent is:

```sh
dotnet build src/ArgueBot.sln -c Release
dotnet run --project src/ArgueBot.Tui -c Release -f net10.0 -- --home .run/home
```

The home above is an isolated development home. Reuse the same home to see the same saved agents and conversations. The TUI automatically starts or attaches to its service. The splash closes after three seconds or any key.

## Appearance

The default dark theme is Quiet Charcoal: workspace `#17191C`, inputs `#25282D`, selection `#303D50`, text `#E3E5E8`, secondary text `#A1A7B0` and muted blue accent `#8AADD8`. Ctrl+T switches between Quiet Charcoal and light mode.

The toolbar, conversation list, history and note composer have no surrounding boxes. One subtle vertical divider separates navigation from history. Dialogs and the splash retain a border. Keyboard focus highlights the selected toolbar button or conversation; a blue chevron identifies the focused conversation list or note area even when empty. The history heading shows the selected conversation title.

## Start with the visible buttons

The full workspace needs at least 60 columns by 20 rows. Below that size, a resize screen replaces clipped controls; F1 Help, F2 Menu and Ctrl+Q remain available. Enlarging the window restores the workspace without discarding its state.

The top of the workspace has buttons you can click. With the keyboard, use Tab or Shift+Tab to move focus and Enter to activate a button. Left/Right moves between buttons in the action row.

| Button | What it does |
| --- | --- |
| Help | Explains navigation, agents, debates, history and administration. |
| Menu | Lists every available action, including search, follow-up questions, history, administration and detaching. |
| New | Starts guided debate setup or creates an empty conversation. |
| Agents | Adds or edits the CLI harnesses and REST models that can speak in a debate. |
| Start debate | Configures a debate for the selected conversation. With no conversation selected, starts new-debate setup. |
| Run controls | Pauses, resumes, interrupts, extends or stops a debate. If the conversation has no debate, offers setup. |

The compact footer reserves **F1 Help** and shows keys for the focused control, followed by global hints as space allows. **F2 Menu** and **Ctrl+Q Detach** remain available throughout the workspace. When the conversation list has focus, the footer shows direct actions such as **d Delete**, **b Debate** and **r Run controls**. F1 lists every shortcut; F2 remains the complete action menu. Menu options wrap instead of losing text at the right edge, and long lists scroll to keep the chosen option visible.

## Set up your first debate

1. Choose **New**, then **Start a debate: guided setup**.
2. If fewer than two agents are enabled, choose **Add or enable an agent**. In Agents, choose **Add agent**, then an installed Claude Code, Codex, Gemini or Mux CLI preset, a REST model endpoint, or a custom CLI harness. Review the fields and save. Repeat until two agents are enabled.
3. In **Question**, enter what you want debated. Enter or the **Next** button advances.
4. In **Speakers**, choose the opening agent, the second agent, and optionally a third agent. Enter opens a dropdown; arrows select an item. Choose a different agent for each position.
5. In **Settings**, set the maximum rounds and the time available to intervene between turns.
6. In **Review**, check the question, speaking order, ending condition and intervention window. Choose **Back** to change anything, **Cancel** to leave, or **Start debate** to submit it.

Back preserves your answers. Expanded fields scroll within the setup step, while Back/Next/Cancel remain visible. The setup does not create the conversation or start a debate until you confirm the Review step. Adding agent profiles saves those reusable configurations separately. If the service refuses a start, the Review screen keeps the error and draft open. A pending start cannot be submitted twice.

The opening agent speaks first in each round. The last selected agent speaks last in each round. Consensus or the maximum round limit ends the debate; there is no independent closing-agent policy.

## Configure an agent

CLI presets fill in the harness name, executable and protocol. The harness must be installed on the machine running the service. The supported CLI protocols supply their normal arguments when you leave the argument list empty. The **Executable (program only)** field takes `claude`, `codex`, or a full program path. Do not enter a whole command such as `claude --verbose`, and do not wrap a path in quotes. Paths containing spaces are allowed.

Put each flag or value in its own **Argument** row. For example, `--output-format` goes in one row and `stream-json` in the next. With no rows, ArgueBot uses its built-in protocol arguments. Adding rows replaces the complete default list, so custom arguments must include the protocol's required output options. An empty row is an actual empty argument. The prompt goes to stdin.

The form catches common commands with flags in the executable field and explains how to correct them. Profile edits apply only to new debates. Existing debates retain their original agent settings, including incorrect ones: stop the old debate and start a new debate after fixing an agent.

For a REST model, choose its provider and enter its endpoint and model identifier. The credential field takes an environment variable **name**, not the secret value. Its value must be available to the running service. If you change the service environment, restart the service before using the new value.

In agent forms, Tab moves to the next field or action. Select **Save**, or use Ctrl+S. Validation and service errors leave the draft open. Escape cancels.

## Work in a conversation

The conversation list receives initial focus. Click a conversation once in the left pane, or select it with Up/Down and press Enter. Actions in this list operate on the highlighted row, even if you have not pressed Enter to open it.

| Key in conversation list | Action |
| --- | --- |
| d or Delete | Confirm permanent deletion of the highlighted conversation |
| b | Set up a debate |
| r | Open run controls |
| p | Pause or resume |
| s | Confirm stopping the highlighted conversation's unfinished debate |
| f | Ask an agent a follow-up |
| l | Read full history |
| g | Inspect generation attempts |
| a | Focus the note composer |
| n | New debate or conversation |
| e | Configure agents |
| / | Search conversations and archived history |
| ? | Help |

These letter keys type normally in the note composer and form fields. Alt+1 focuses conversations, Alt+2 history and Alt+3 the note composer. Escape returns from the workspace to conversations. In dialogs, Escape still closes the innermost interaction first. Tab and Shift+Tab traverse the workspace controls.

Global shortcuts include F8 Stop debate, Ctrl+N New, Ctrl+B Debate, Ctrl+R Run controls, Ctrl+P Pause/resume, Ctrl+Y Follow-up, Ctrl+L History, Ctrl+G Attempts, Ctrl+F Search and Ctrl+E Agents. Ctrl+A is reserved for text selection in inputs; follow-up previously used that chord. The open conversation has a `>` marker. Unsent notes are kept separately for each open conversation when you switch between them. The right pane displays its messages. With history focused, arrows and PageUp/PageDown scroll, Home/End jumps to the beginning/latest content, and Enter or l opens full history.

The note box adds context to the conversation. Sending a note does **not** start an agent reply. Choose **Start debate** to run the agents, or **f** in the conversation list or **Ctrl+Y** for a follow-up.

Use **Run controls** to intervene in a debate. **l** in the conversation list or **Ctrl+L** opens complete messages when the workspace shows only a preview. **g** in the conversation list or **Ctrl+G** shows execution and source-context details.

The connection line above the footer reports connection status and history counts. To leave, use **Menu → Detach**. Detaching closes the TUI while service-owned work continues.

## Delete a conversation

Highlight the conversation in the left pane and press **d** or **Delete**, then review its name and the permanent-deletion warning. You can also open it and choose **Menu → Delete this conversation**. **Cancel** is the default. Choose **Delete permanently** to remove the conversation and its history.

If a debate is active, paused or stopping, Delete offers **Stop debate and delete permanently**. Confirm to let the TUI stop the debate, wait for the service, and automatically finish deletion. **Cancel** is the default and leaves both the conversation and debate untouched. If stopping takes longer than five seconds, choose **Keep waiting, then delete** or cancel deletion; cancelling at that point keeps the conversation but does not undo the stop request. A failed stop never proceeds with deletion.

You can still stop without deleting: highlight the conversation and press lowercase **s**, or use **F8** for the open conversation.

## Read long help and messages

Help pages, message readers and debate review open at the beginning and wrap to the available width. Use arrows, PageUp/PageDown, Home/End or the mouse wheel to scroll. A scrollbar shows when more text is available; clicking the scrollbar changes the scroll position. Tab reaches the Close or wizard navigation buttons, and Escape returns.

F1 Help contains eight scrollable guides: navigation, agent setup, debate setup/controls, final instructions after consensus, history/attempts, troubleshooting, administration/recovery, and theme/detaching. Each guide explains the workflow and keys, with examples and recovery steps. Escape returns to the contents, then to the workspace.

For the web interface, `./go-web.sh` or `go-web.bat` builds the React dashboard and C# solution, then starts or attaches to the shared service and prints its plain dashboard URL. Open it directly. The default local workspace requires no authentication, account, tenant, pairing code or special link. The service continues in the background after the launcher exits. Run it again to reuse a running service. Both interfaces accept `--home PATH`; `--port N` selects the web service's port only when starting an absent service.

See [the usability review](TUI-USABILITY.md) for the comparison with Armada, Armor, S3Drive and Mux and the verification scope.

## Interaction conventions

- **F1 works inside dialogs**, including forms and debate setup. It opens interaction help without discarding the draft. F1, Escape or Back to dialog returns to the same state.
- **Escape closes the innermost interaction first.** If a dropdown is open, Escape closes that dropdown. Press Escape again to cancel the surrounding dialog.
- **Menus identify complete options.** Labels wrap, selection scrolls by option, and scrollbars do not activate choices when clicked.
- **Long text is read-only.** Help, history and review open at the beginning, expose a scrollbar when needed and support keyboard and mouse scrolling.
- **Pending work has one owner.** A pending start cannot be submitted twice. Failure stays on the Review screen with the draft intact.
- **Conversation selection is stable.** A late response for another conversation cannot replace the history you opened. Switching conversations preserves their separate note drafts.

For Mux, choose **Agents → Add agent → Mux (installed CLI)**. The default executable is `mux`; change it to the installed executable path if needed. Empty arguments use `print --output-format text --no-stats --no-session-persistence`. Mux reads the debate prompt from stdin and uses its own endpoint configuration and authentication on the service machine. To choose a named endpoint or model, supply the complete argument list, for example `print`, `--output-format`, `text`, `--no-stats`, `--no-session-persistence`, `--endpoint`, `my endpoint`, `--model`, `my-model`, with each value in its own argument field. Keep text output selected; JSON/JSONL output would appear literally in the transcript. ArgueBot retains the conversation history and supplies context on each turn; Mux session persistence is disabled by default. Tool permissions follow Mux configuration unless explicitly overridden in the saved arguments.

Named harness setup uses the shared `assets/harness-defaults.json` catalog in the web, TUI, service and execution fallback. Selecting Claude Code, Codex, Gemini CLI or Mux fills executable and arguments. Claude Code includes `--dangerously-skip-permissions` and Codex includes `--yolo` to run debate turns unattended. Generic CLI remains manually configured. The service fills omitted values for API/CLI create and update too, and returns the persisted defaults. Explicit custom arguments are preserved and replace the full list. Existing run snapshots are not rewritten.

Identity administration has been removed. Backup and restore are also removed. Users, roles, security audit and their shortcuts are not product features. Use `stop-web.sh` or `stop-web.bat` to stop the selected service.
