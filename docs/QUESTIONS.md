# Questions for you

When a debate question is vague, a human expert would ask a few questions before answering. ArgueBot agents can do the same. An agent can stop in the middle of its turn, ask you one to four questions, wait for your answers and carry on with them. Questions arrived in v2.3.0. The design is recorded in [PROMPTING_THE_USER_PLAN.md](../PROMPTING_THE_USER_PLAN.md) and [adr/0007-user-questions.md](adr/0007-user-questions.md).

## What an agent can ask

Each request carries one to four questions, and each question is one of four kinds:

| Kind | You answer with |
| --- | --- |
| Text | Free text, up to the length the agent allows (8,000 characters at most). |
| Single choice | One option. The agent may mark one option as recommended and may allow an Other answer in your own words. |
| Multi-select | Several options, within the minimum and maximum the agent sets, optionally with Other. |
| Confirm | Yes or no. |

The agent can add a short note explaining why it asks. Questions can be optional; you can leave those unanswered.

## Answering

You always answer in a dialog. No page shows answer controls inline.

- **Dashboard.** When you are looking at the conversation whose agent is waiting, the question dialog opens by itself. Anywhere else, a notice appears with **Answer**, which opens the dialog, and **Go to conversation**. The **Questions** page in the sidebar lists every question, open ones first; choose a row to open it. The sidebar badge and the page title show how many questions wait for you, and Home lists the newest. In the transcript, the agent's question appears as a tool record with **Open the question**, and a banner above the transcript offers **Answer**. When several questions wait in one conversation, the dialog steps through them ("1 of 3"). The dashboard checks the question change feed every five seconds while its tab is visible; a notice appears for questions asked while it is open, and questions that were already waiting when you opened it show in the badge, on Home and on the Questions page.
- **TUI.** When the open conversation's debate starts waiting, the question dialogs open by themselves: an overview with the agent's note, then one dialog per question (a list for single choice, a checklist for multi-select where Space toggles an option, a text prompt, or Yes and No), then a summary to send. The menu entry **Questions waiting for you** lists open questions across conversations, and run controls offer **Answer this debate's questions**.
- **CLI.** `questions list`, `questions show ID`, `questions answer ID` (asks each question on a terminal, or takes `--choice scope=prod`, `--text key=...`, `--other key=...`, `--confirm key=yes`, `--skip-question key` or `--file answer.json`), `questions skip ID [--note TEXT]`, `questions defer ID`, `questions delete ID [ID...]` and `questions watch`. `questions wait CONVERSATION RUN` follows a debate and asks its questions on a terminal; without a terminal, or with `--no-input`, it prints the waiting question and exits with code 4.
- **API and C# client.** `GET /v2.0/api/questions`, `GET /v2.0/api/questions/{questionId}`, `POST .../answer`, `POST .../skip`, `POST .../defer`, `DELETE /v2.0/api/questions/{questionId}`, the open-count summary, and a change feed (`GET /v2.0/api/questions/events`, also as a server-sent event stream). See [REST_API.md](../REST_API.md). `ArgueBotClient` has a method for each.

The dialog offers three ways out:

- **Send answers** sends your answers. The agent continues with them.
- **Skip** tells the agent no answer is available, with an optional note such as what to assume. The agent continues on stated assumptions.
- **Answer later** ends the agent's waiting turn now. The step picks up again once you answer.

Closing the dialog (Close, Esc or clicking outside it) leaves the question open. It is never a skip.

## Deleting questions

Once a question is answered, skipped, expired or cancelled and its debate has finished, you can delete it: from the row's menu on the **Questions** page, by selecting several rows and choosing **Delete multiple**, with `questions delete ID [ID...]` or with `DELETE /v2.0/api/questions/{questionId}`. Deleting removes the question, its options and its answers from the Questions page and the API. Your answer stays in the conversation transcript as a message. Open questions cannot be deleted (409 `question_open`; answer or skip them first), and neither can questions from a debate that is still running (409 `active_run`), because later turns may still use the answer. Deletions are recorded in the audit log as `question_deleted`.

If someone else in your tenant answers first, your dialog says so and shows their answer; your answer is not sent. The first answer wins.

## Waiting, and what happens to the debate

While an agent waits for you, the run shows **Waiting for you**.

The agent's call is first held open for up to `questionHoldSeconds` (default 300). If you answer within that time, the agent receives your answer in the same turn. During that time the agent's turn timeout and the debate's wall-clock budget are paused, and the run gives up its capacity slot so other debates can run.

If the hold time ends, you choose Answer later, you pause the debate or the service stops, the question is *parked*. The agent ends its turn in a sentence, no vote or step is counted, and the debate keeps waiting. When you answer, the debate repeats that step with your answer, by itself unless the debate's **After you answer** setting says to stay paused. If you paused the debate, answering does not resume it; press Resume when you are ready. A paused debate cannot resume while a question is still open.

Stopping a debate cancels its open questions. With `questionExpirySeconds` set, an unanswered question expires and the agent is told you did not answer in time.

At most `maxLiveQuestionHolds` (service setting, default 4) calls are held open across the service; beyond that, new questions park at once.

## Opening questions

New debates start with an **opening questions** (intake) step: before round 1, the orchestrator reads the question and either asks you about it or decides it is clear and starts the panel. Its answers become context for every agent. The step uses one orchestration call. Turn it off in the debate's settings under **Questions for you**, or with `"intake": "off"` in the debate policy. Debates created before 2.3.0 have it off.

## Settings

In the dashboard these are under **Questions for you** in the debate settings, and in the debate policy for the API and CLI.

| Policy field | Default | Meaning |
| --- | --- | --- |
| `userQuestions` | `all` | Who may ask: `all`, `orchestrator` or `off`. |
| `intake` | `ask_if_needed` | Opening questions before round 1, or `off`. |
| `maxIntakeQuestions` | 4 | Questions in the opening request, 1 to 4. |
| `questionHoldSeconds` | 300 | How long a turn waits for your answer, 0 to 1,800. 0 parks at once. |
| `questionExpirySeconds` | 0 | Seconds before an unanswered question expires, 0 (never) to 2,592,000. |
| `resumeOnAnswer` | true | Continue by itself once the last waiting question is answered. |
| `maxQuestionRequestsPerTurn` | 2 | Requests per agent turn, 0 to 4. |
| `maxQuestionRequestsPerRun` | 20 | Requests per debate, 0 to 200. |

## Who sees answers

Any member of the tenant can answer a question in a conversation they can open; the service records who answered. The Questions page lists your own conversations by default and everyone's in the tenant on request. An answer is added to the conversation as a message from you that every agent sees, unless the agent asked for an answer only it can see. Shared transcript links show questions and answers like other moderator messages.

## Secrets and trust

Question text is written by an agent and is shown as data. ArgueBot never needs a password, token or key in an answer. When a question reads like a request for one, the dialog shows a stronger warning; skip it or describe what you can share instead. Answers are stored in the database and, for tenant administrators, in request history, like any other text you send.

Browser notifications are optional. Turn them on under **Account > Question notifications**; they appear only while ArgueBot is in a background tab, and the choice applies to that browser.

## Replacing clarification requests

Before 2.3.0, agents could call `request_clarification` with a free-text question that you answered through an interjection. Version 2.3.0 removes that tool, the `GET .../runs/{runId}/clarifications` route, `clarificationId` and `skipClarification` on interjections, the `clarification_required` error and `debate clarifications`. Upgrading moves every existing request into a one-question text question with its status and answer, and a debate that was waiting on one now waits for input with the question parked.
