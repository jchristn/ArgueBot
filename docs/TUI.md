# Using the C# terminal interface

The C# TUI and web dashboard are the supported conversation interfaces. Both use the same persistent service.

Build and launch from the repository root with `./go-tui.sh` on macOS/Linux or `go-tui.bat` on Windows. Both build the service and TUI first and forward TUI arguments, for example `./go-tui.sh --home .run/home`. An optional first argument selects the target framework, `net8.0` or `net10.0` (the default), for example `./go-tui.sh net8.0 --home .run/home`. A manual equivalent is:

```sh
dotnet build src/ArgueBot.sln -c Release
dotnet run --project src/ArgueBot.Tui -c Release -f net10.0 -- --home .run/home
```

The home above is an isolated development home. Reuse the same home to see the same saved agents and conversations. The TUI automatically starts or attaches to its service. The splash closes after three seconds or any key.

## Sign in

The TUI starts on a sign-in page whenever it has no valid session. It shows the default administrator (`admin@arguebot` / `password`) and default token (`arguebotadmin`) at the top of the form and beside each field.

- **Server URL** is filled with the local service the TUI started or attached to. To use another ArgueBot service, enter its address, for example `https://arguebot.example.com`, or start with `--server URL`. Plain `http://` to another machine is refused unless you tick **Allow plain HTTP** or start with `--allow-http`, because your password would cross the network unencrypted.
- Enter an **Email** and **Password** (Ctrl+R shows or hides it), or a **bearer token** instead. Ctrl+S signs in; Esc quits.
- If your email and password work in several tenants, a list asks which tenant to use.
- Problems appear at the top of the form and keep what you typed: an invalid URL, a server that does not answer, a host name the server does not allow, a wrong password, a disabled account or tenant, and too many attempts each have their own message.

After signing in, the session (never the password) is remembered for this home in `client-session.json`, readable only by you, and refreshed while the TUI runs, so the next start skips the page. `ARGUEBOT_TOKEN` signs in with a token without saving it. The connection line at the bottom shows who you are and which tenant you are in.

**Menu > Account** shows who you are signed in as, changes your password, manages your bearer tokens (create, reveal again, disable or delete) and sessions, signs out and switches server. Signing out or a revoked session returns to the sign-in page. Tenant administrators also get **Menu > Users** and **Audit log**; system administrators get **Tenants**, where they can open any tenant's users. With a conversation open, **Menu > Share a read-only link** creates a public link that expires after the time you choose, and lists and revokes earlier links.

## Appearance

The default dark theme is Quiet Charcoal: workspace `#17191C`, inputs `#25282D`, selection `#303D50`, text `#E3E5E8`, secondary text `#A1A7B0` and muted blue accent `#8AADD8`. Ctrl+T switches between Quiet Charcoal and light mode.

The toolbar, conversation list, history and note composer have no surrounding boxes. One subtle vertical divider separates navigation from history. Dialogs and the splash retain a border. Keyboard focus highlights the selected toolbar button or conversation; a blue chevron identifies the focused conversation list or note area even when empty. The history heading shows the selected conversation title.

## Start with the visible buttons

The full workspace needs at least 60 columns by 20 rows. Below that size, a resize screen replaces clipped controls; F1 Help, F2 Menu and Ctrl+Q remain available. Enlarging the window restores the workspace without discarding its state.

The top of the workspace has buttons you can click. With the keyboard, use Tab or Shift+Tab to move focus and Enter to activate a button. Left/Right moves between buttons in the action row.

| Button | What it does |
| --- | --- |
| Help | Ten task-ordered topics, from a first-debate walkthrough to setup, steering, follow-ups, troubleshooting and a keyboard shortcut reference. |
| Menu | Every action, grouped under headings: the open conversation, conversations, setup, and view and help. Run controls are grouped the same way: run, steer the debate, inspect and working memory. |
| New | Starts guided debate setup or creates an empty conversation, asking for its title, an optional opening question and, when custom prompts exist, which prompt each debate step uses. |
| Agents | Adds, edits or checks the agents that can speak in a debate: CLI programs with an optional secret and model, or HTTP models. |
| Start debate | Configures a debate for the selected conversation. With no conversation selected, starts new-debate setup. |
| Run controls | Pauses, resumes, interrupts, extends or stops a debate. If the conversation has no debate, offers setup. |

The main menu (F2) also opens **Prompts**: view a prompt's text with its parameter legend, duplicate any prompt (including the read-only factory defaults), delete a custom prompt, or edit a custom prompt's name and description and load new text from a file. See [PROMPTS.md](PROMPTS.md).

The compact footer reserves **F1 Help** and shows keys for the focused control, followed by global hints as space allows. **F2 Menu** and **Ctrl+Q Detach** remain available throughout the workspace. When the conversation list has focus, the footer shows direct actions such as **d Delete**, **b Debate** and **r Run controls**. F1 lists every shortcut; F2 remains the complete action menu. Menu options wrap instead of losing text at the right edge, and long lists scroll to keep the chosen option visible.

## Set up your first debate

1. Choose **New**, then **Start a debate: guided setup**.
2. If fewer than two agents are enabled, choose **Add or enable an agent**. In Agents, choose **Add agent**, then an installed Claude Code, Codex, Gemini or Mux CLI preset, an HTTP model, or another CLI program. Review the fields, optionally choose a model and a **Secret** (select a stored secret when running in Docker or to override the credentials used by default by the agent), and save. Repeat until two agents are enabled.
3. In **Question**, enter what you want debated. Enter or the **Next** button advances.
4. In **Speakers**, check every agent for the panel, up to the service limit, then choose an explicit orchestrator model. Space toggles a checkbox; Enter opens the orchestrator dropdown and arrows select an item.
5. In **Settings**, set the maximum rounds and the time available to intervene between turns. Optionally enter the checked seat numbers in the opening order you want.
6. In **Review**, check the question, speaking order, ending condition and intervention window. Choose **Back** to change anything, **Cancel** to leave, or **Start debate** to submit it.

Back preserves your answers. Expanded fields scroll within the setup step, while Back/Next/Cancel remain visible. The setup does not create the conversation or start a debate until you confirm the Review step. Adding agent profiles saves those reusable configurations separately. If the service refuses a start, the Review screen keeps the error and draft open. A pending start cannot be submitted twice.

The opening order rotates each round. The orchestrator asks a question, collects responses, proposes a synthesis and requests explicit votes. Every active seat must agree on the same proposal before the service records consensus; the round limit preserves disagreement.

## Configure an agent

CLI presets fill in the agent name, executable and protocol. The program must be installed on the machine running the service. The supported CLI protocols supply their normal arguments when you leave the argument list empty. The **Executable (program only)** field takes `claude`, `codex`, or a full program path. Do not enter a whole command such as `claude --verbose`, and do not wrap a path in quotes. Paths containing spaces are allowed.

Put each flag or value in its own **Argument** row. For example, `--output-format` goes in one row and `stream-json` in the next. With no rows, ArgueBot uses its built-in protocol arguments. Adding rows replaces the complete default list, so custom arguments must include the protocol's required output options. An empty row is an actual empty argument. The prompt goes to stdin.

The form catches common commands with flags in the executable field and explains how to correct them. Profile edits apply only to new debates. Existing debates retain their original agent settings, including incorrect ones: stop the old debate and start a new debate after fixing an agent.

For a REST model, choose its provider and enter its endpoint and model identifier. The secret field takes an environment variable **name**, not the secret value. Its value must be available to the running service. If you change the service environment, restart the service before using the new value.

In agent forms, Tab moves to the next field or action. Select **Save**, or use Ctrl+S. Validation and service errors leave the draft open. Escape cancels.

## Work in a conversation

The conversation list receives initial focus. Click a conversation once in the left pane, or select it with Up/Down and press Enter. Actions in this list operate on the highlighted row, even if you have not pressed Enter to open it.

| Key in conversation list | Action |
| --- | --- |
| d or Delete | Confirm permanent deletion of the highlighted conversation |
| b | Set up a debate using the original saved question, editable before Review |
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

For the web interface, `./go-web.sh` or `go-web.bat` (optionally followed by `net8.0` or `net10.0`) builds the React dashboard and C# solution, then starts or attaches to the shared service and prints its plain dashboard URL. Open it directly. The default local workspace requires no authentication, account, tenant, pairing code or special link. The service continues in the background after the launcher exits. Run it again to reuse a running service. Both interfaces accept `--home PATH`; `--port N` selects the web service's port only when starting an absent service.

See [the usability review](TUI-USABILITY.md) for the comparison with Armada, Armor, S3Drive and Mux and the verification scope.

## Interaction conventions

- **F1 works inside dialogs**, including forms and debate setup. It opens interaction help without discarding the draft. F1, Escape or Back to dialog returns to the same state.
- **Escape closes the innermost interaction first.** If a dropdown is open, Escape closes that dropdown. Press Escape again to cancel the surrounding dialog.
- **Menus identify complete options.** Labels wrap, selection scrolls by option, and scrollbars do not activate choices when clicked.
- **Long text is read-only.** Help, history and review open at the beginning, expose a scrollbar when needed and support keyboard and mouse scrolling.
- **Pending work has one owner.** A pending start cannot be submitted twice. Failure stays on the Review screen with the draft intact.
- **Conversation selection is stable.** A late response for another conversation cannot replace the history you opened. Switching conversations preserves their separate note drafts.

For Mux, choose **Agents → Add agent → Mux (installed CLI)**. The default executable is `mux`; change it to the installed executable path if needed. Empty arguments use `print --output-format text --no-stats --no-session-persistence`. Mux reads the debate prompt from stdin and uses its own endpoint configuration and authentication on the service machine. To choose a named endpoint or model, supply the complete argument list, for example `print`, `--output-format`, `text`, `--no-stats`, `--no-session-persistence`, `--endpoint`, `my endpoint`, `--model`, `my-model`, with each value in its own argument field. Keep text output selected; JSON/JSONL output would appear literally in the transcript. ArgueBot retains the conversation history and supplies context on each turn; Mux session persistence is disabled by default. Tool permissions follow Mux configuration unless explicitly overridden in the saved arguments.

Named CLI program setup uses the shared `assets/harness-defaults.json` catalog in the web, TUI, service and execution fallback. Selecting Claude Code, Codex, Gemini CLI or Mux fills executable and arguments. Claude Code includes `--dangerously-skip-permissions` and Codex and Mux include `--yolo` to run debate turns unattended. Generic CLI remains manually configured. The service fills omitted values for API/CLI create and update too, and returns the persisted defaults. Explicit custom arguments are preserved and replace the full list. Existing run snapshots are not rewritten.

Identity administration has been removed. Backup and restore are also removed. Users, roles, security audit and their shortcuts are not product features. Use `stop-web.sh` or `stop-web.bat` to stop the selected service.

A failed turn displays TURN FAILED followed by its cause and recovery guidance in the transcript. Ctrl+G opens the matching generation-attempt history for inspection. After correcting an agent output limit, stop the old run and use b to start another with the original question and current agent settings. Ctrl+N starts a separate conversation with a blank question.

## Resources

Choose **Menu > Resources** to manage [resources](RESOURCES.md), the named markdown documents agents can read with `read_resource`. The list shows each resource's name and title, `[locked]` for locked resources and `[agent: NAME]` when an agent wrote the latest revision. **Add a resource** asks for a name, title and description, then opens the text editor. **Import markdown files** takes one or more paths separated by `;`; front matter can set the name, title, description, tags and locked flag.

Choose a resource to:

- **Read** it, with its title, description, tags, revision and author.
- **Edit text** in a multi-line editor. Type or paste, then press **Ctrl+S** (or choose **Save**) to save; **Escape** cancels. If someone changed the resource since you opened it, the save is refused and nothing is overwritten.
- **Edit name, title, description and tags**.
- **Lock** it so agents can read but not change it, or **Unlock** it.
- Open **History, changes and revert**: choose a revision to see a line diff against the current text (`-` removed since that revision, `+` added), then **Restore revision N** to save its text as a new revision.
- **Export** it to a markdown file, or **Delete** it with its history.

The debate wizard's settings step sets **Resource access** (Read by default, Read and write, or Off), optional lists of visible resource names and tags, and the write limits. The review step summarizes them.

## Reusable configuration and debate memory

Choose **Agents → Connections, models and secrets** to manage endpoint, model and secret forms. HTTP bindings can combine bearer, API-key, header and query authentication. CLI programs are configured on agents, whose arguments remain literal, including empty arguments. Secret paste fields are masked and can read a local file. Values never appear in configuration lists. Choose a secret, then **Reveal value** and confirm to show its decrypted value in a panel. See [the v2 guide](MULTIPLE_AGENTS.md) for the resource order and imported Claude/Codex login behavior.

Each context submission chooses **Use in next turn** or **Interrupt now**, with pinned variants available. Cancelling that choice preserves the draft. Run controls expose panel membership changes, proposals and explicit positions, interjection receipts, summaries and original sources, clarification answers or skips, and summary exclusion/rebuilding. Long readers and choice lists scroll within the terminal. A paused debate stays paused until you explicitly resume it.

The terminal's post-implementation checks render all new forms in dark/light themes at 60, 80 and 120 columns, and panel setup at 4, 16 and 64 seats. Rendering evidence is separate from provider compatibility; see [the implementation plan](../archive/MULTIPLE_AGENTS_PLAN.md).

For a saved HTTP endpoint, choose **Check connection / list models** to contact it with its configured auth bindings without invoking a completion. The result lists advertised IDs only; tool support and context capacity remain declarations until separately verified. The composer uses **Add context**, and every submission to a v2 run presents its own delivery choice.
