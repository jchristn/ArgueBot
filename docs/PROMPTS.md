# Debate prompts

Each protocol 2 debate step sends an instruction prompt to one model. The conversation itself (turns, moderator context, summaries) is sent separately as context after these instructions. Prompts are stored in the database and managed from the dashboard's **Prompts** page, the TUI's **Prompts** menu, the CLI and the API. Workspace prompts belong to a tenant: every member of the tenant can use, edit and delete them, and other tenants never see them. Factory defaults are the same read-only set in every tenant.

## Steps

| Step (`purpose`) | Sent to | Reply the protocol expects |
| --- | --- | --- |
| `question` | Orchestrator | JSON with `question` and `rationale` |
| `response` | Each agent | Free text: the agent's position and evidence |
| `synthesis` | Orchestrator | JSON with `proposal` |
| `ratification` | Each agent | JSON with `proposalId`, `position` and `rationale` |
| `compaction` | Orchestrator | JSON working memory with `summary`, seven `sections` and `sourceIds` |

ArgueBot validates structured replies. When a reply does not have the required shape, it asks the model for a corrected reply up to the debate's step retry limit (`maxStepRetries`, default 3), then pauses the debate. Keep the reply instructions from the factory prompt when you write your own.

## Factory defaults and custom prompts

Every workspace has one factory prompt per step (IDs `pmt_factory_question`, `pmt_factory_response` and so on). Their text is the built-in protocol 2 text. The proposal step must answer the original question, and agents agree only to a final answer to it, because unanimous agreement ends the debate; agreeing on a method or sub-question continues to the next round. Factory prompts are read-only: they cannot be edited or deleted, and each release refreshes them on startup. To change one, duplicate it; the copy is a custom prompt you can rename, describe and edit. A prompt's step is fixed when it is created.

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
| `{resources}` | all (opt-in) | One line per [resource](RESOURCES.md) the debate's agents can read: name, title and description, at most 100 lines and 8,000 characters. Empty when the debate's resource access is off. The factory prompts do not use it |

`GET /v2.0/api/prompt-parameters` and `prompts parameters` in the CLI return the same list.

## Referring to resources

A prompt can tell agents where to find more context instead of including it, for example "if you need to know more about billing, call `read_resource` with name `billing`". The agent reads the [resource](RESOURCES.md) only when it needs it, provided the debate's resource access is Read or Read and write. Agents' role instructions work the same way. Add `{resources}` to list every resource the debate can see.

When a prompt names a resource with `read_resource` (for example `read_resource("billing")` or "call read_resource with name billing") and no resource has that name in your tenant, reading or saving the prompt returns a warning in `warnings`, and the dashboard editor shows it. The prompt still saves, so you can write the prompt before the resource.

## Choosing prompts

**New conversation** has a **Debate prompts** section with one choice per step; unchanged steps use the factory default. The conversation stores the choices (`promptIds` on the conversation). **Start debate** shows the conversation's choices and lets you change them for that debate (`promptIds` on the run request). When a debate starts it freezes the text of every chosen prompt with its revision; the debate panel's **Prompts used** section lists them. Editing or deleting a prompt later never changes a started debate.

## Interfaces

- Dashboard: **Prompts** page (view, duplicate, edit, delete), plus the pickers in **New conversation** and **Start debate**.
- TUI: **Prompts** in the main menu views text and parameters, duplicates and deletes, and edits a custom prompt's name and description; new prompt text loads from a file. New conversation offers factory defaults or a choice per step.
- CLI: `prompts list [PURPOSE]`, `show ID`, `parameters`, `add`, `update ID`, `duplicate ID`, `delete ID REVISION`.
- API: `/v2.0/api/prompts` and `/v2.0/api/prompt-parameters`; see [REST_API.md](../REST_API.md).
