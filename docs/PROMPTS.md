# Debate prompts

Each protocol 2 debate step sends an instruction prompt to one model. The conversation itself (turns, moderator context, summaries) is sent separately as context after these instructions. Prompts are stored in the database and managed from the dashboard's **Prompts** page, the TUI's **Prompts** menu, the CLI and the API.

## Steps

| Step (`purpose`) | Sent to | Reply the protocol expects |
| --- | --- | --- |
| `question` | Orchestrator | JSON with `question` and `rationale` |
| `response` | Each agent | Free text: the agent's position and evidence |
| `synthesis` | Orchestrator | JSON with `proposal` |
| `ratification` | Each agent | JSON with `proposalId`, `position` and `rationale` |
| `compaction` | Orchestrator | JSON working memory with `summary`, seven `sections` and `sourceIds` |

ArgueBot validates structured replies. When a reply does not have the required shape, it asks the model once for a corrected reply, then pauses the debate. Keep the reply instructions from the factory prompt when you write your own.

## Factory defaults and custom prompts

Every workspace has one factory prompt per step (IDs `pmt_factory_question`, `pmt_factory_response` and so on). Their text is exactly what ArgueBot sent before prompts were editable. Factory prompts are read-only: they cannot be edited or deleted, and each release refreshes them on startup. To change one, duplicate it; the copy is a custom prompt you can rename, describe and edit. A prompt's step is fixed when it is created.

Deleting a custom prompt returns any conversation that chose it to the factory default. Debates that already started keep their copy.

## Parameters

Write a parameter as `{name}`. Write `{{` and `}}` for literal braces. Values are substituted in one pass, so text inside a value is never expanded again. The editor's legend lists the parameters available for the selected step; saving rejects unknown parameters, parameters that have no value in that step, and missing required ones.

| Parameter | Steps | Value |
| --- | --- | --- |
| `{agent_name}` | all | Name of the agent receiving the prompt |
| `{role_instructions}` | all | That agent's role instructions |
| `{question}` | all | The debate question frozen at start |
| `{panel}` | all | Comma-separated active agent names |
| `{round}`, `{max_rounds}` | all | Current round and the round limit |
| `{context_epoch}`, `{roster_revision}` | all | Context and roster revision counters |
| `{guidance}` | question, synthesis, compaction | The matching orchestration guidance from the debate's execution settings |
| `{current_question}` | response (required) | The orchestrator's current question |
| `{proposal}`, `{proposal_id}` | ratification (both required) | The exact proposal and its ID, as JSON strings |

`GET /v2.0/api/prompt-parameters` and `prompts parameters` in the CLI return the same list.

## Choosing prompts

**New conversation** has a **Debate prompts** section with one choice per step; unchanged steps use the factory default. The conversation stores the choices (`promptIds` on the conversation). **Start debate** shows the conversation's choices and lets you change them for that debate (`promptIds` on the run request). When a debate starts it freezes the text of every chosen prompt with its revision; the debate panel's **Prompts used** section lists them. Editing or deleting a prompt later never changes a started debate.

## Interfaces

- Dashboard: **Prompts** page (view, duplicate, edit, delete), plus the pickers in **New conversation** and **Start debate**.
- TUI: **Prompts** in the main menu views text and parameters, duplicates and deletes, and edits a custom prompt's name and description; new prompt text loads from a file because the terminal has no multi-line editor. New conversation offers factory defaults or a choice per step.
- CLI: `prompts list [PURPOSE]`, `show ID`, `parameters`, `add`, `update ID`, `duplicate ID`, `delete ID REVISION`.
- API: `/v2.0/api/prompts` and `/v2.0/api/prompt-parameters`; see [REST_API.md](../REST_API.md).
