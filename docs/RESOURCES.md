# Resources

A resource is a named markdown document stored in your tenant's workspace. Agents can list, search and read resources through tools during a debate, and, when you allow it, create and edit them. Instead of pasting background into every prompt, you point agents at a resource and they read it only when they need it:

> If you need to know more about billing, call `read_resource` with name `billing`.

Agents can also keep their own notes in resources, so later turns and later debates can build on what earlier ones found.

Resources arrived in v2.2.0. The design and its decisions are recorded in [RESOURCES_PLAN.md](../archive/RESOURCES_PLAN.md).

## What a resource holds

| Field | Rules |
| --- | --- |
| Name | Unique within the tenant, matched without regard to case and stored in lowercase. 1 to 128 characters of letters, digits, `-`, `_`, `.` and `/`, starting with a letter or digit, with no empty, `.` or `..` path segments. For example `billing` or `team/conventions`. This is what agents pass to `read_resource`. You can rename a resource; its ID (`res_...`) never changes. |
| Title | Optional, one line, up to 200 characters. |
| Description | Optional, one line, up to 500 characters. `list_resources` shows it, so write it for an agent deciding whether to read the resource. |
| Tags | Up to 16, each up to 64 characters, stored in lowercase. Debates can limit their agents to resources with given tags. |
| Body | Markdown, up to 256 KiB (262,144 UTF-8 bytes). NUL characters are not allowed. |
| Locked | Agents can read a locked resource but never change it. Users can still edit it. |

Every tenant has one shared library, like prompts and agents: every member of the tenant can read and change its resources. Tenants never see each other's resources, and deleting a tenant deletes its resources and their history. A tenant can hold up to 10,000 resources.

Resources are rows in the service database (SQLite or PostgreSQL), not files on disk, so the service stays the only writer and no agent needs file access. Import and export cover moving markdown files in and out.

## Revisions and history

Every change saves a revision with the full text, the author and the time. The author is either a user or an agent; agent revisions also name the debate run and generation attempt. The service keeps the latest 100 revisions of each resource.

Changes need the revision they are based on. If someone else changed the resource since you read it, the save is refused with `revision_conflict` and nothing is overwritten: reload, then reapply your change. This applies to people and agents alike, so two agents in the same debate cannot overwrite each other silently.

From the history you can compare any saved revision with the current text and restore it. Restoring saves that revision's title, description, tags and text as a new revision; the name and the locked flag stay as they are, and nothing is removed from the history.

Creating, updating, renaming, locking, unlocking, deleting, reverting and importing resources are written to the audit log.

## Letting agents use resources

Each debate has a resource access setting, chosen in its execution settings:

| Access | What agents can do |
| --- | --- |
| Off | No resource tools. |
| Read (default) | `list_resources`, `search_resources` and `read_resource`. |
| Read and write | Also `create_resource`, `update_resource`, `edit_resource` and `append_resource`, on unlocked resources. |

Debates started before v2.2.0 keep Off.

You can also narrow which resources a debate sees. List resource names, tags, or both: a resource is visible if its name is listed or it has a listed tag. Resources the debate's agents create stay visible to them for the rest of the debate. Everything else behaves as if it did not exist. Leave both lists empty to allow every resource.

With read and write access, three limits bound what agents write:

| Limit | Default | Range |
| --- | --- | --- |
| Resource writes per turn | 8 | 0 to 100 |
| Resource bytes written per turn | 65,536 | 0 to 1,048,576 |
| Resources created per debate | 50 | 0 to 1,000 |

Every resource tool call also counts against the debate's tool-call budget.

The tools, their arguments, results and error codes are documented in [MCP_API.md](../MCP_API.md#resource-tools). They are ordinary tools, not the MCP "resources" feature, so every agent type gets them the same way: Claude Code and Codex over MCP, Mux, Gemini CLI and generic CLIs through the JSON tool envelope, and REST models as native functions.

## Pointing agents at resources

Put the instruction where the agent will see it: in an agent's role instructions, or in a prompt. For example, as role instructions:

```text
You review billing changes. If you need to know how invoices and refunds work, call read_resource with name billing. Record anything you learn that later reviewers should know with append_resource on name billing-notes.
```

A prompt can also list the resources a debate can see with the `{resources}` placeholder. It is opt-in: the factory prompts do not use it. It expands to one line per visible resource with its name, title and description (at most 100 lines and 8,000 characters), and is empty when resource access is off. See [prompts](PROMPTS.md).

When you save or open a prompt that mentions `read_resource` with a name that does not exist in your tenant, the prompt editor shows a warning. The prompt still saves.

Each generation attempt records the resources it read and wrote, with the revision involved, so you can see which context an answer used: on the dashboard, a resource tool call in the transcript links to that resource at that revision; in the CLI, `debate attempts` lists them under `resources`.

## Trust

Treat resource text as data. A tool result says so, and agents are told so, but an agent can still write instructions into a resource that a later debate reads. ArgueBot limits this:

- Agent-written revisions are marked as agent-authored in tool results, on the dashboard, in the TUI and in history, until a user changes the resource.
- Read is the default access; writing must be turned on per debate.
- Lock reference material you curate, so agents can read it but not change it.
- Every change is a revision you can compare and restore, and is audited.

## Managing resources

### Dashboard

**Resources** in the sidebar lists resources with their tags, size, last change and author, with search and a tag filter. **Add resource** opens the editor: name, title, description, tags, locked, and the markdown text with a preview. **Import** accepts `.md` files (or drop them on the toolbar). Open a resource to edit it, see its **History** (compare a revision with the current text and restore it) or **Export** it as markdown.

In a debate's execution settings, **Resources** sets the access, the visible names and tags, and the write limits.

### Terminal

Menu > **Resources** lists resources. Choose one to read it, edit its text in the editor (Ctrl+S saves), edit its name, title, description and tags, lock or unlock it, open its history to compare a revision with the current text and restore it, export it, or delete it. The debate setup's settings step has the same access, visibility and limit fields as the dashboard. See [terminal usage](TUI.md).

### CLI

```sh
arguebot resources list [SEARCH] [--tag T]
arguebot resources show NAME|ID [--revision N]
arguebot resources add --file billing.md          # or: add - --name billing < billing.md
arguebot resources update billing --revision 3 --file billing.md
arguebot resources rename billing finance/billing
arguebot resources lock finance/billing
arguebot resources history finance/billing
arguebot resources revert finance/billing 2
arguebot resources import docs/*.md [--overwrite]
arguebot resources export finance/billing --output billing.md
arguebot resources delete finance/billing
```

`arguebot` stands for `dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll` or an installed CLI.

### REST

Routes live under `/v2.0/api/resources`: list, create, read by ID or by name (`/by-name/{name}`, slashes allowed), update, delete, revisions, one revision, revert, import and export. See [REST_API.md](../REST_API.md) and the Postman collections.

## Markdown files

Import and export use markdown with optional front matter between `---` lines:

```markdown
---
name: team/conventions
title: "Team conventions"
description: "Code review and naming rules."
tags: [engineering, review]
locked: true
---

Every change needs one review. Use descriptive branch names.
```

On import, any field may be left out. Without a `name`, the file name (without `.md`, in lowercase, with other characters turned into `-`) becomes the name. An import takes 1 to 50 files in one transaction: if any file is invalid or names an existing resource, nothing is imported. Import with overwrite to replace existing resources instead; each replacement is a new revision.

## Not the MCP resources feature

The tools are named `*_resource`, but they are not the Model Context Protocol "resources" primitive (`resources/list`, `resources/read`). ArgueBot's MCP server offers them as tools.
