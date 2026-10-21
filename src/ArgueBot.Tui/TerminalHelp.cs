namespace ArgueBot.Tui
{
    internal static class TerminalHelp
    {
        internal static string[] Titles { get; } = new[]
        {
            "Navigation and conversations",
            "Configure agents and harnesses",
            "Start and control a debate",
            "After consensus: final instructions and follow-ups",
            "History, messages and attempts",
            "Troubleshooting failed or paused debates",
            "Theme and detaching"
        };

        internal static string[] Pages { get; } = new[]
        {
            """
            Find your way around

            The left pane lists saved conversations. The right pane shows the open conversation's history. The note composer at the bottom saves context. The buttons at the top expose common actions; F2 opens the full action menu.

            Tab and Shift+Tab move between controls. Up/Down chooses a conversation; Enter opens it. Clicking a row opens it too. The > marker identifies the open conversation. The highlighted row can be different: letter shortcuts in the list act on the highlighted row, while global shortcuts and the menu use the open conversation.

            Jump directly with Alt+1 for conversations, Alt+2 for history, or Alt+3 for the note composer. Escape in the workspace returns to conversations. In a dialog it returns one level; if a dropdown is open, Escape closes that dropdown first.

            Conversation-list keys (lowercase)

            n: New debate or empty conversation. b: Set up a debate in the highlighted conversation. e: Configure agents. /: Search titles and content, then choose active or archived history. Alt+Left/Right changes conversation pages.

            r: Run controls. p: Pause or resume. s: Confirm stopping a debate. Follow-up in the top action bar (Ctrl+Y), or f in the list: ask one agent another question. l: Read full history. g: Inspect generation attempts. a: Move to the note composer. ?: Help. These letters type normally in text fields.

            Notes and replies

            Type in the note composer and press Enter to save a note. A note adds context; it does not ask an agent to answer. Use f in the list or Ctrl+Y to request an answer. Unsent note drafts are kept separately when you switch conversations.

            Delete safely

            Press d or Delete in the list. Check the named conversation before confirming permanent deletion of it and its messages. Cancel is selected by default. If a run blocks deletion, choose Stop debate and delete permanently to let ArgueBot stop it, wait and finish deletion. Cancelling that offer leaves both untouched. If you cancel deletion after stopping has begun, the conversation is kept but the stop request remains in effect.

            The footer shows keys for the focused control. F1 opens Help and F2 lists all actions even when not every hint fits. Resize to at least 60 columns by 20 rows for the workspace.

            Press Escape to return to the Help contents.
            """,
            """
            Give the debate agents to work with

            Open Agents with Ctrl+E, the Agents button, or e in the conversation list. Choose Add agent, then a Claude Code, Codex, Gemini, Mux, REST model or custom CLI preset. A debate needs at least two enabled agents. A follow-up needs one. Choose an existing profile to edit it, enable it or delete the reusable profile; historical run snapshots remain readable.

            CLI setup: program first, arguments separately

            Executable means the program name, such as claude or codex, or its full path on the machine running the service. Do not put a whole command such as claude --verbose here. Do not wrap the path in quotes. A path containing spaces is still one executable value.

            Each Argument row holds exactly one flag or value. For example, --output-format goes in one row and stream-json in the next. Spaces inside a row remain part of that one argument. An empty row is passed as an empty argument.

            With no rows, the selected CLI protocol supplies its built-in arguments. Adding any custom rows replaces the entire default list; include the complete argument list and the required output-format options. Choose the protocol matching the program, since ArgueBot must understand its output. The prompt is sent through standard input. The working directory is optional and refers to the service machine.

            Installation and sign-in

            Presets configure a profile; they do not install the program or sign in for you. The executable must be available to the service, with the harness's authentication already configured for the account running it. A full path can resolve differences between your terminal's PATH and the service's PATH. Harness tool permissions come from the harness and its saved arguments.

            REST setup

            Choose the provider, its HTTP(S) endpoint and the exact model identifier. The credential field takes an environment variable name, never the secret itself. That variable must be present in the service environment. Restart the service after changing its environment. REST agents can read and write files, list directories and run programs through service-owned local tools. Set Working directory to the project they should use; empty uses the service directory. Tool calls and results appear as Local tool entries in history. They run as the service account, just like CLI harnesses. Stop cancels a running tool process; pause waits for the current turn. A model must support native tool calls. The service local execution setting controls both CLI harnesses and REST tools.

            Save and apply changes

            Tab moves between fields. Ctrl+S or Save saves; Escape cancels. Validation and service errors keep the draft open so you can correct it. Disabling an agent excludes it from new runs.

            Profile changes affect new debates and follow-ups. Existing runs keep their original agent settings. After fixing a profile, stop the old run and start a new debate; Resume retries with the old settings. Profile edits do not rewrite history.

            Press Escape to return to the Help contents.
            """,
            """
            Start a debate with a reviewed setup

            Choose New or Ctrl+N, then Start a debate: guided setup. To use an existing conversation, open it and press Ctrl+B or choose Start debate. In the list, b uses the highlighted conversation. If too few agents are enabled, setup offers to open Agents first.

            1. Question: New (Ctrl+N) starts with a blank question; b or Ctrl+B in an existing conversation preserves its original question in an editable field. Enter the issue the agents should discuss. Starting in an existing conversation keeps its history as context. The question shown on Review is submitted. Enter or Next advances.

            2. Speakers: select the opening agent, second agent and optional third agent. Enter opens a dropdown, arrows select and Enter confirms. Choose distinct agents. The opening agent speaks first in each round; the last selected agent speaks last. There is no separate closing-agent setting.

            3. Settings: choose the round limit and the intervention time between turns. A round gives each selected speaker a turn. The intervention window gives you time to add context or use run controls before generation continues.

            4. Review: read the question, speaker order and settings. Back or Alt+Left returns without losing answers; Next or Alt+Right advances. Only Start debate on Review submits the run. A failed submission leaves the review and error open. Do not keep pressing Start while a request is pending.

            Control an ongoing debate

            r in the list or Ctrl+R opens run controls. Available commands depend on the run's state. Pause after turn lets the current answer finish before pausing. Ctrl+P or p pauses, or resumes a run already paused. Continue immediately skips an intervention window.

            Interrupt current generation cancels the current answer and retains partial text. Resume retries the unfinished turn and may incur another model charge. It does not reconstruct the remainder of an interrupted answer. Resume uses the run's original agent settings.

            A private directive goes only to the chosen participant when its context is assembled; it is different from a shared note. Extend by two rounds increases the round limit when the run's state permits it. Extending is not a general way to reopen a consensus-completed run.

            Stop and completion

            s in the list or F8 for the open conversation asks you to confirm stopping. Partial answers remain in history. Wait for Debate stopped before treating it as stopped. You may also press d and accept the combined stop-and-delete offer when removing the conversation.

            A run ends on consensus or its round limit. Consensus records the agents' declared agreement; it is not an independent verification of their claims. For final instructions after completion, use a follow-up as described in the next Help topic.

            Press Escape to return to the Help contents.
            """,
            """
            Turn the discussion into a final deliverable

            After consensus or the round limit, use a follow-up to ask one agent to summarize, revise or act on the discussion. You do not need to restart the debate just to ask for a final answer.

            1. Highlight the conversation and press lowercase f. Alternatively, open the conversation and press Ctrl+Y, or choose Ask an agent a follow-up question in F2 Menu.

            2. Choose the enabled agent that should respond. A follow-up runs one agent, not all the debate participants.

            3. Enter your instructions in Follow-up question and press Enter. This field can contain instructions rather than a literal question. Escape cancels before submission.

            Example final instructions

            Using the agreed conclusions, write the final implementation plan. Include the chosen approach, unresolved risks, concrete steps and a test checklist.

            Or: Summarize the consensus for a nontechnical reader in five short paragraphs, and distinguish agreed facts from assumptions.

            What the agent receives

            The selected agent receives eligible conversation context within the service's context limits. The follow-up question and answer become part of the same conversation. Do not assume every message fits an unlimited context window. Inspect generation attempts if you need to check the exact source messages supplied.

            Notes versus follow-ups

            The note composer saves shared context without triggering an answer. To get an answer, send the instructions through f or Ctrl+Y. Notes can be useful before a later follow-up, but Enter in the note composer alone does not execute them.

            If a run is still active or paused

            A conversation cannot start another run while unfinished work occupies it. Resume the existing run if you want it to finish, or use s/F8 to stop it before starting the follow-up. Do not choose Delete if you want to keep the discussion.

            If you want several agents to discuss a new issue together, use b/Ctrl+B for a fresh debate instead. Follow-ups use the selected agent's current reusable profile; resuming an older debate retains that older run's saved profiles.

            Press Escape to return to the Help contents.
            """,
            """
            Read the discussion without losing the details

            The workspace shows a live history view, which can contain shortened previews of long messages. Alt+2 focuses it. Use Up/Down, PageUp/PageDown, Home and End to scroll or jump to the latest content. Enter or l while history is focused opens the full history workflow.

            Full messages

            l in the conversation list, Ctrl+L, or Menu > Read full message history opens paged history. Choose the message you want to read. A preview notice means you should use this full-message reader for the complete text. The reader is read-only: typing or pasting does not edit the saved answer.

            Inside long help pages and message readers, arrows scroll a line, PageUp/PageDown scroll a page, Home/End jumps to the beginning/end, and the mouse wheel scrolls. A scrollbar indicates more text. Tab moves focus to Close; Escape returns to the previous view.

            Understand message states

            Streaming means text is still arriving. Completed means the invocation finished. Interrupted is partial text retained after cancellation or a related interruption. Failed means generation did not complete successfully; a failed row may contain partial text or no text. A later successful retry does not erase the earlier failed attempt.

            Inspect attempts

            g in the list or Ctrl+G opens generation attempts. Select a current or historical run, then an attempt. Refresh or change pages as needed. The inspector shows the run's original question, participant snapshot, state, safe error code, elapsed time, prompt version/digest and ordered source-message IDs.

            Use this to distinguish a launch failure from a later output failure, see which invocation was retried, and identify what context was supplied. Source IDs refer to the stored messages; context limits can exclude older messages. A prompt digest identifies prompt text, not proof that an answer is correct.

            Private directives

            Directives are addressed to one selected participant. They are not shared notes. Another participant's generation context excludes directives meant for someone else. Inspect the attempt's source context when checking what an agent actually received.

            The web dashboard attached to the same service can display the same durable conversations. Closing a reader or switching interfaces does not delete the history.

            Press Escape to return to the Help contents.
            """,
            """
            Find the cause before retrying

            A Failed row is a recorded invocation, not necessarily the current run's final state. First open g/Ctrl+G, select the relevant run and inspect its latest attempt. Check the safe error code and participant snapshot. Earlier failed attempts remain visible even after a successful retry.

            The agent produced no answer

            This means the failed message has no answer text. It does not identify the cause by itself. The attempt inspector and local service logs provide more information. A very quick failure often points to program startup or configuration, but the recorded error is the evidence to use.

            Program could not start / Win32Exception

            Check Agents with e/Ctrl+E. Executable must be only a program name or path, with flags in separate Argument rows. Confirm the program exists on the service machine and the working directory exists. A full path can fix PATH differences. After changing a profile, stop the old run and start a new debate; Resume keeps its original saved configuration.

            Process exited / authentication problems

            Check that the harness is signed in for the service's operating-system account and that its arguments match the installed version. For REST agents, check the endpoint, model and credential environment variable. Do not enter secret values in an environment-reference field. A restarted service is needed to pick up changes to its process environment.

            Invalid output / InvalidDataException

            The program started but ArgueBot could not accept its output. Check that the selected protocol matches the executable and that custom arguments retain the required output format. Custom arguments replace all defaults. If a corrected configuration still fails, retain the attempt ID and service diagnostics for investigation rather than repeatedly retrying.

            Active or paused run blocks an action

            Use r/Ctrl+R to inspect controls. Resume if you want to continue the same run, or s/F8 to stop it and retain the conversation. Delete offers Stop debate and delete permanently when unfinished work blocks removal. Cancel preserves the conversation; confirm the combined action only if its history should be removed.

            Connection or permission errors

            A connection error can mean the service is restarting or unavailable. Preserve drafts and wait for the connection line to recover. Permission refusals come from the service; hiding or changing a TUI control does not grant access. Ask the service administrator to check the account when necessary.

            Retrying a generation can repeat external work or incur another model charge. ArgueBot does not automatically retry an interrupted turn. Use an explicit Resume only when that is the action you want.

            Press Escape to return to the Help contents.
            """,
            """
            Make the workspace comfortable

            Ctrl+T switches between Quiet Charcoal and the light theme. Focus changes the selected control's appearance; conversation, history and note headings also show an accent indicator when their pane is focused. The footer changes to show keys for the current control.

            Tab/Shift+Tab moves between controls. Alt+1, Alt+2 and Alt+3 jump to conversations, history and notes. Use the mouse for buttons and conversation rows if you prefer; keyboard actions remain available through F1 Help and F2 Menu.

            The workspace needs at least 60 columns by 20 rows. If the terminal is smaller, a resize screen replaces clipped controls. Increase the window size to return without discarding workspace state. F1, F2 and Ctrl+Q remain available on that screen.

            Leave without stopping the debate

            Ctrl+Q or Menu > Detach closes the TUI. The service owns execution, so accepted debates and background jobs continue while the terminal is closed. Detach does not mean Stop, and closing a dialog does not withdraw a submitted operation.

            To stop an unfinished debate before leaving, use s in the conversation list or F8 for the open conversation and confirm. To delete it too, use d and review the deletion and combined stop/delete confirmations. A deleted conversation and its history cannot be recovered through this interface.

            Return to the same data

            Launch the TUI with the same application home and service connection to reopen saved conversations and profiles. A different home selects a different local installation's data. The default home can be overridden with --home or ARGUEBOT_HOME. The web interface attached to that service uses the same durable history.

            Saved conversations and sent notes remain after detaching. Unsent composer drafts are kept when switching conversations within the running TUI; do not rely on them surviving exit. Save a note first if you want its text stored with the conversation.

            Help inside dialogs

            F1 opens interaction help without discarding a form or wizard draft. F1, Escape or Back to dialog returns to that draft. Escape closes the innermost dropdown or dialog first. When reading a help topic, Escape returns to the contents; Escape again returns to the workspace.

            Press Escape to return to the Help contents.
            """
        };
    }
}
