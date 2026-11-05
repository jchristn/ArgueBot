namespace ArgueBot.Tui
{
    /// <summary>Task-ordered help topics for the terminal interface.</summary>
    internal static class TerminalHelp
    {
        internal static string[] Titles { get; } = new[]
        {
            "Get started: your first debate",
            "Find your way around",
            "Set up agents",
            "Models, connections, credentials and prompts",
            "Start a debate",
            "Watch and steer a debate",
            "After a debate: follow-ups and reading",
            "Troubleshooting",
            "Keyboard shortcuts",
            "Themes, detaching and your data"
        };

        internal static string[] Pages { get; } = new[]
        {
            """
            Get started: your first debate

            1. Add agents. Press Ctrl+E (or Menu > Agents) and choose Add agent. Pick a preset such as Claude Code or Codex, check the executable, and save. You need at least two enabled agents. The program must already be installed and signed in on the machine running the service.

            2. Start a debate. Press Ctrl+N and choose Start a debate: guided setup. Enter the question, choose the agents and an orchestrator, set the number of rounds, review, and choose Start debate.

            3. Watch it. Turns appear in the history pane as they finish. Type in the note box at the bottom to add context; ArgueBot asks whether it applies at the next turn or interrupts the current one.

            4. Use the result. When the debate ends, press Ctrl+Y to ask one agent for a final deliverable, such as a plan or a summary.

            The service keeps running when you close the TUI (Ctrl+Q). Open the web dashboard on the same service to read the debate as a full-page transcript.

            Press Escape to return to the Help contents.
            """,
            """
            Find your way around

            The screen has three parts: the conversation list on the left, the open conversation's history on the right, and the note box at the bottom. The buttons along the top run common actions. F2 opens the Menu with everything else, grouped by what you want to do.

            Moving around

            Tab and Shift+Tab move between controls. Alt+1 jumps to conversations, Alt+2 to history, Alt+3 to the note box. Escape returns to the conversation list; in a dialog it goes back one level and closes an open dropdown first.

            The conversation list

            Up and Down highlight a conversation; Enter or a click opens it. The > marker shows the open conversation and * marks a pinned one. Letter keys in the list act on the highlighted conversation (see Keyboard shortcuts), while Ctrl shortcuts and the Menu act on the open one. Alt+Left and Alt+Right change pages.

            The footer shows keys for the focused control. The workspace needs at least 60 columns by 20 rows; a smaller terminal shows a resize screen until you enlarge it.

            Press Escape to return to the Help contents.
            """,
            """
            Set up agents

            An agent is anything that can take a seat in a debate: a command-line program such as Claude Code, Codex, Gemini CLI or Mux, or a model reached over HTTP. Open Agents with Ctrl+E, choose Add agent, then a preset. Choose an existing agent to edit, enable, disable or delete it. Disabled agents are left out of new debates.

            Command-line agents

            Executable is only the program name (claude, codex) or its full path on the service machine. Do not put flags there, and do not quote a path that contains spaces. Each Argument row holds one flag or one value: --output-format in one row, stream-json in the next. With no rows, the preset's defaults are used; adding any row replaces all of them, so include the full list. Working directory is where the program runs.

            Presets do not install or sign in a program. It must be available to the service and signed in for the account that runs the service. A full path avoids PATH differences between your terminal and the service.

            HTTP models

            Choose the provider, the endpoint and the exact model name. For an API key, either set up a reusable model with a stored credential (see Models, connections, credentials and prompts) or enter the name of an environment variable on the service machine, never the key itself. HTTP agents can read and write files and run programs through the service's local tools, in their working directory.

            Saving

            Ctrl+S saves and Escape cancels. A validation error keeps your draft open. Changes apply to new debates; a debate already running keeps the settings it started with.

            Press Escape to return to the Help contents.
            """,
            """
            Models, connections, credentials and prompts

            Menu > Models, connections and credentials manages reusable configuration. Menu > Prompts manages the instructions each debate step sends.

            Credentials

            Import a credential once and reuse it. Kinds: Claude Code OAuth token (from claude setup-token), Codex login (the auth.json file from a dedicated codex login), bearer token, header key and value, and query string key and value. Values are encrypted on the service. Choosing a credential then Reveal value shows it after you confirm. In Docker or under another account, command-line agents need a stored credential because there is no local sign-in.

            Connections and models

            An HTTP endpoint is an Ollama or OpenAI-compatible server, with optional authentication bindings that use stored credentials. A CLI harness is a reusable command-line program with its arguments and credential. A model names what an agent runs: a model ID on an endpoint, or a harness. Agents can select a reusable model. Model checks make a real call and can cost money; saving never calls a model.

            Encryption keys

            Credential encryption keys shows the active key and offers rotation. After rotating, back up the protected secrets directory.

            Prompts

            Each debate step (orchestrator question, agent response, proposal, vote, working-memory summary) uses a prompt. Factory prompts are read-only; choose one and Duplicate to make an editable copy. View shows the text and the parameters a step supports, such as {question} or {current_question}. Edit changes a copy's name and description and can load new text from a file. When you create a conversation with New, you can choose a prompt for each step.

            Press Escape to return to the Help contents.
            """,
            """
            Start a debate

            Press Ctrl+N and choose Start a debate: guided setup, or open a conversation and press Ctrl+B to debate inside it with its history as context. If fewer than two agents are enabled, setup offers to open Agents first. To create an empty conversation instead, choose Create an empty conversation: it asks for a title, an optional opening question and, when you have custom prompts, which prompt each step uses.

            The four steps

            1. Question: what the agents should debate. In an existing conversation it starts with that conversation's question and stays editable.

            2. Speakers: check at least two agents and choose the orchestrator. The orchestrator asks the questions, drafts proposals and summarizes; it does not vote. The opening speaker rotates each round.

            3. Settings: the round limit and the pause between turns, which gives you time to add context.

            4. Review: check everything, then Start debate. Back (Alt+Left) and Next (Alt+Right) keep your answers. If starting fails, the error names the step to fix.

            How a round works

            The orchestrator asks one pointed question, every agent answers, the orchestrator proposes an answer, and every agent votes. The debate reaches consensus only when every active agent explicitly agrees with the same proposal; otherwise it continues until the round limit.

            Press Escape to return to the Help contents.
            """,
            """
            Watch and steer a debate

            Turns appear in the history pane as they finish. A turn still being written shows Responding. Long turns show a preview; press Ctrl+L to read them in full.

            Add context

            Type in the note box and press Enter. While a debate is running, ArgueBot asks when it applies: Use in next turn lets the current answer finish; Interrupt now stops it and continues with your context. New context means agents vote again on a fresh proposal.

            Run controls (Ctrl+R)

            Run: pause after the current turn, resume, continue immediately (skip the pause between turns), interrupt the current answer, extend by two rounds, or stop. Ctrl+P pauses or resumes; F8 stops after you confirm.

            Steer the debate: send a private directive to one agent, edit the proposal under vote (every agent votes again), change agents at the next safe point, or answer an agent's clarification request.

            Inspect: proposals and votes, how your context was delivered, summaries with their original sources, and budget usage.

            Working memory: rebuild or exclude a summary. Pause first; original turns are never deleted.

            Resuming retries the unfinished turn with the debate's original settings and may repeat work or cost.

            Press Escape to return to the Help contents.
            """,
            """
            After a debate: follow-ups and reading

            A debate ends on consensus or at its round limit. Consensus records that the agents agreed; it does not prove they are right.

            Ask for a deliverable

            Press Ctrl+Y (or f in the list), choose one agent, and type instructions, for example: Using the agreed conclusions, write the implementation plan with risks, steps and a test checklist. The agent sees the conversation within its context limit, and its answer joins the conversation. To have several agents discuss a new question, start another debate with Ctrl+B instead.

            Read the conversation

            Ctrl+L opens the full history; choose any message to read it in full. In readers, arrows and PageUp/PageDown scroll, Home and End jump, and Escape goes back. For a document-style transcript with rounds, the final proposal and every vote, open the conversation in the web dashboard and choose Read transcript. It can copy as Markdown or print to PDF.

            Inspect what happened

            Ctrl+G opens generation attempts: each model call with its state, error, timing, prompt and the exact messages it was given. Failed attempts stay listed after a successful retry.

            If the conversation still has a paused or running debate, resume or stop it (Ctrl+R) before starting a follow-up.

            Press Escape to return to the Help contents.
            """,
            """
            Troubleshooting

            Start with Ctrl+G: choose the run, then its latest attempt, and read the error and explanation.

            The program could not start

            In Agents, check that Executable is only a program name or path, with flags in separate rows, and that the program and working directory exist on the service machine. After fixing an agent, stop the old debate and start a new one; Resume keeps the old settings.

            Sign-in or exit errors

            Check that the program is signed in for the account running the service and that its arguments suit the installed version. For HTTP models check the endpoint, model name and credential. Environment changes need a service restart.

            Malformed output (InvalidDataException)

            The program started but its output could not be read. Check that the preset matches the program and that custom arguments keep the required output format. The service keeps the raw output in its local runner-failure log for diagnosis.

            Provider refused, rate limited or unavailable

            The attempt says which. Fix credentials, wait for the rate limit, or try later, then resume.

            A paused or running debate blocks something

            Resume it, or stop it with F8. Deleting offers to stop and delete in one step.

            Connection lost

            The service may be restarting. Drafts are kept; wait for the status to recover.

            Press Escape to return to the Help contents.
            """,
            """
            Keyboard shortcuts

            Anywhere

            F1 Help. F2 Menu. Ctrl+Q detach. Ctrl+T switch theme. Tab and Shift+Tab move between controls. Alt+1 conversations, Alt+2 history, Alt+3 note box. Escape goes back.

            The open conversation

            Ctrl+N new debate or conversation. Ctrl+B start a debate here. Ctrl+R run controls. Ctrl+P pause or resume. F8 stop. Ctrl+Y follow-up question. Ctrl+L full history. Ctrl+G generation attempts. Ctrl+E agents. Ctrl+F search.

            Conversation list (lowercase letters, highlighted conversation)

            n new. b start a debate. e agents. / search. r run controls. p pause or resume. s stop. f follow-up. l full history. g generation attempts. d delete. a go to the note box. ? help. Alt+Left and Alt+Right change pages. These letters type normally in text fields.

            Dialogs and forms

            Ctrl+S saves. Escape cancels or goes back. F1 shows help without losing your draft. In the setup wizard, Alt+Left and Alt+Right move between steps.

            Press Escape to return to the Help contents.
            """,
            """
            Themes, detaching and your data

            Theme

            Ctrl+T, or Menu > Switch theme, toggles between the dark and light themes.

            Leave without stopping anything

            Ctrl+Q, or Menu > Detach, closes the TUI. The service keeps running, so debates continue. To stop a debate before leaving, press F8 (or s in the list) and confirm.

            Deleting

            Press d in the list and check the named conversation before confirming; Cancel is selected by default. Deletion removes the conversation and its messages permanently.

            Your data

            Conversations, agents and settings live in the application home (~/.arguebot unless you pass --home or set ARGUEBOT_HOME). Start the TUI with the same home to see the same data. The web dashboard attached to the same service shows the same conversations. Sent notes are saved; unsent drafts survive switching conversations but not exiting.

            Press Escape to return to the Help contents.
            """
        };
    }
}
