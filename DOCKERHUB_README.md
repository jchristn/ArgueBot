<p align="center"><img src="https://raw.githubusercontent.com/jchristn/arguebot/main/icon.png" alt="ArgueBot" width="160" height="160" /></p>

# ArgueBot

ArgueBot puts AI agents into a moderated debate. A panel of agents (Claude Code, Codex, Gemini CLI, Mux, or REST models served through Ollama or an OpenAI-compatible endpoint) answers an orchestrator's questions, the orchestrator proposes an answer, and every agent votes. Consensus requires every active agent to agree with the same proposal. You can add context while they work, edit the proposal under vote, change the panel, and read the whole debate afterwards as a transcript.

This image runs the ArgueBot service, which owns all execution and storage, together with the React web dashboard, the native CLI and terminal interface, and pinned Claude Code, Codex and Gemini CLIs.

## Run it

Use the Compose file from the repository:

```sh
git clone https://github.com/jchristn/arguebot.git
cd arguebot
cp docker/.env.example docker/.env      # optional: port and provider settings
docker compose -f docker/compose.yaml up -d
```

Then open `http://127.0.0.1:4317/dashboard`.

Or run the image directly:

```sh
docker run -d --name arguebot \
  -p 127.0.0.1:4317:4317 \
  -v arguebot-data:/data \
  -v arguebot-keys:/data/secrets \
  -v arguebot-work:/work \
  jchristn77/arguebot:v2.1.0
```

## First steps

1. On the **Participants** page, open the **Credentials** tab and add a Claude Code token (`claude setup-token`) or a Codex login. A container has no local sign-in, so command-line agents need a stored credential.
2. Add a CLI harness for each program and pick its credential, or an HTTP endpoint for Ollama or another provider.
3. Add models and agents, then start a debate from **Conversations**.

## Image details

| Item | Value |
| --- | --- |
| Port | `4317` (dashboard and API) |
| Data | `/data` (database, settings, logs) |
| Encryption key | `/data/secrets` (mount separately) |
| Agent working files | `/work` |
| Runs as | user `arguebot` (UID 10001) |
| Health | `GET /health`; Compose waits up to 30 seconds for startup |
| Database | SQLite in `/data` by default; PostgreSQL through the service settings |

Keep the three volumes together across updates. The encryption key on `arguebot-keys` is what makes stored credentials readable; a database backup without it cannot recover them.

## Environment

| Variable | Purpose |
| --- | --- |
| `ARGUEBOT_PORT` | Host port that Compose publishes on `127.0.0.1` (default `4317`). |
| Provider variables such as `GEMINI_API_KEY` or `OPENAI_BASE_URL` | Passed to an agent only when its harness lists the name under Inherited environment names. |

## Security

ArgueBot v2.0 has no sign-in: anyone who can reach the port controls the workspace. Agents run commands and edit files inside the container, and the dashboard can reveal stored credentials. Compose publishes the port on `127.0.0.1` only. Reach it remotely through an SSH tunnel or a private network, not by publishing it to the internet. User accounts, bearer-token credentials and a login page are in development for v2.1.0.

## Learn more

- [Docker setup](https://github.com/jchristn/arguebot/blob/main/docker/README.md)
- [Debate guide](https://github.com/jchristn/arguebot/blob/main/docs/MULTIPLE_AGENTS.md)
- [README](https://github.com/jchristn/arguebot/blob/main/README.md) and [CHANGELOG](https://github.com/jchristn/arguebot/blob/main/CHANGELOG.md)
