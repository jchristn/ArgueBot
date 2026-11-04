<p align="center"><img src="https://raw.githubusercontent.com/jchristn/arguebot/main/icon.png" alt="ArgueBot" width="160" height="160" /></p>

# ArgueBot

Put Claude, Codex, Gemini and locally served models into a moderated debate. Choose an orchestrator to ask questions and synthesize proposals. Add context while agents work, inspect original evidence and require every active seat to agree before declaring consensus.

The v2.0.0 implementation preview includes a shared service, React dashboard, terminal interface and scriptable CLI. The service owns execution and persistence; changing clients does not stop a debate. Reusable model, endpoint and harness definitions support Ollama and OpenAI-compatible HTTP protocols, combined authentication bindings and write-only Claude/Codex credential imports.

No published v2 image is available yet. Build the repository's application image locally:

```sh
docker compose -f docker/compose.yaml build
docker compose -f docker/compose.yaml up -d
```

Open `http://127.0.0.1:4317/dashboard`, configure Connections and models, then create participant profiles and start a panel with an explicit orchestrator. Compose binds the open workspace to host loopback. Provider access is configured on the service, independently of workspace access.

Named volumes preserve the database, encryption key and agent working files. Preserve `arguebot-data`, `arguebot-keys` and `arguebot-work` together across updates. The encryption key is mounted separately at `/data/secrets`. Use `docker/update.sh` or `docker/update.bat` to recreate the local stack without deleting volumes. Inspect provider login failures and interrupted turns before explicitly resuming work.

See the repository's [Docker setup](https://github.com/jchristn/arguebot/blob/feature/v2.0.0/docker/README.md), [v2 guide](https://github.com/jchristn/arguebot/blob/feature/v2.0.0/docs/MULTIPLE_AGENTS.md) and [implementation plan](https://github.com/jchristn/arguebot/blob/feature/v2.0.0/MULTIPLE_AGENTS_PLAN.md). Linux arm64 container acceptance has passed with fictional provider fixtures, including persistent credentials and missing-key recovery. Live-provider and other architecture verification remain release gates.
