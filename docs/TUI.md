# Using the C# terminal interface

The C# TUI is the replacement conversation interface currently being developed. The older `go-terminal` scripts still launch the TypeScript interface.

Build and launch the C# interface from the repository root:

```sh
dotnet build src/ArgueBot.sln -c Release
dotnet run --project src/ArgueBot.Tui -c Release -f net10.0 -- --home .run/home
```

The home above is an isolated development home. Reuse the same home to see the same saved agents and conversations. The TUI automatically starts or attaches to its service. The splash closes after three seconds or any key.

## Start with the visible buttons

The top of the workspace has buttons you can click. With the keyboard, use Tab or Shift+Tab to move focus and Enter to activate a button. Left/Right moves between buttons in the action row.

| Button | What it does |
| --- | --- |
| Help | Explains navigation, agents, debates, history and administration. |
| Menu | Lists every available action, including search, follow-up questions, history, administration and detaching. |
| New | Starts guided debate setup or creates an empty conversation. |
| Agents | Adds or edits the CLI harnesses and REST models that can speak in a debate. |
| Start debate | Configures a debate for the selected conversation. With no conversation selected, starts new-debate setup. |
| Run controls | Pauses, resumes, interrupts, extends or stops a debate. If the conversation has no debate, offers setup. |

The footer starts with **F1 Help** and **F2 Menu**. Shortcuts are alternatives to the visible actions.

## Set up your first debate

1. Choose **New**, then **Start a debate: guided setup**.
2. If fewer than two agents are enabled, choose **Add or enable an agent**. In Agents, choose **Add agent**, then an installed Claude Code, Codex or Gemini CLI preset, a REST model endpoint, or a custom CLI harness. Review the fields and save. Repeat until two agents are enabled.
3. In **Question**, enter what you want debated. Enter or the **Next** button advances.
4. In **Speakers**, choose the opening agent, the second agent, and optionally a third agent. Enter opens a dropdown; arrows select an item. Choose a different agent for each position.
5. In **Settings**, set the maximum rounds and the time available to intervene between turns.
6. In **Review**, check the question, speaking order, ending condition and intervention window. Choose **Back** to change anything, **Cancel** to leave, or **Start debate** to submit it.

Back preserves your answers. The setup does not create the conversation or start a debate until you confirm the Review step. Adding agent profiles saves those reusable configurations separately.

The opening agent speaks first in each round. The last selected agent speaks last in each round. Consensus or the maximum round limit ends the debate; there is no independent closing-agent policy.

## Configure an agent

CLI presets fill in the harness name, executable and protocol. The harness must be installed on the machine running the service. The supported CLI protocols supply their normal arguments when you leave the argument list empty. Custom arguments replace those defaults and are passed as ordered literal arguments. The prompt goes to stdin.

For a REST model, choose its provider and enter its endpoint and model identifier. The credential field takes an environment variable **name**, not the secret value. Its value must be available to the running service. If you change the service environment, restart the service before using the new value.

In agent forms, Tab moves to the next field or action. Select **Save**, or use Ctrl+S. Validation and service errors leave the draft open. Escape cancels.

## Work in a conversation

Select a conversation in the left pane and press Enter. The open conversation has a `>` marker. The right pane displays its messages.

The note box adds context to the conversation. Sending a note does **not** start an agent reply. Choose **Start debate** to run the agents, or **Menu → Ask an agent a follow-up question** for a follow-up.

Use **Run controls** to intervene in a debate. **Menu → Read full message history** opens complete messages when the workspace shows only a preview. **Menu → Inspect generation attempts** shows execution and source-context details.

The connection line above the footer reports connection status and history counts. To leave, use **Menu → Detach**. Detaching closes the TUI while service-owned work continues.
