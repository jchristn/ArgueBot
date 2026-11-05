# ArgueBot container workspace

The application image `jchristn77/arguebot` contains the service, built web dashboard, native CLI/TUI, Node.js and pinned Claude Code, Codex and Gemini CLIs. It is published for `linux/amd64` and `linux/arm64`.

From the repository root, run the published image:

```sh
docker compose -f docker/compose.yaml up -d
docker compose -f docker/compose.yaml ps
```

To build and run this checkout instead, add the build override:

```sh
docker compose -f docker/compose.yaml -f docker/compose.build.yaml build
docker compose -f docker/compose.yaml -f docker/compose.build.yaml up -d
```

Maintainers publish a new image with `./build-all.sh v2.1.0` (or `build-all.bat v2.1.0` on Windows). It builds both architectures on the `cloud-jchristn77-jchristn77` buildx builder, pushes `jchristn77/arguebot:<tag>` and `:latest`, then pulls them back. `docker/update.sh` and `docker/update.bat` pull the published image and recreate the stack without deleting volumes.

Open `http://127.0.0.1:4317/dashboard` and sign in. A new data volume starts with the default administrator **`admin@arguebot`** / **`password`** and the bearer token **`arguebotadmin`**, shown on the sign-in page. Anyone who can reach the port can sign in with them, so change the password before exposing the service. The service listens on the container network and Compose publishes it only on host loopback.

### Reaching the container from another machine

1. Publish the port beyond loopback: create an untracked `docker/compose.remote.yaml` that sets `services.arguebot.ports` to `["4317:4317"]` (or a LAN address) and add `-f docker/compose.remote.yaml` to your `docker compose` commands, or put a reverse proxy on the host in front of the loopback port.
2. Allow the host name people will use: set `ARGUEBOT_ALLOWED_HOSTS=arguebot.example.com` (comma-separated for several) in `docker/.env`. Without it, requests for that name get 403 `invalid_host`. If the names aren't known in advance, `ARGUEBOT_ALLOWED_HOSTS=*` accepts any name; change the default administrator password first, because sign-in is then the only guard. If your proxy rewrites `Host`, have it send `X-Forwarded-Host` so share links use the public name.
3. Use HTTPS. Every request carries a password or token. A TLS-terminating proxy works; forward the original `Host` header and `X-Forwarded-Proto: https`. For example, with Caddy: `arguebot.example.com { reverse_proxy 127.0.0.1:4317 }`.
4. Sign in from the TUI or CLI with `--server https://arguebot.example.com`. They refuse plain HTTP to a remote server unless you pass `--allow-http`.

To change the published port or pass provider settings, copy `docker/.env.example` to `docker/.env` (untracked) and edit it. Compose reads `ARGUEBOT_PORT` for the loopback port and passes every variable in that file into the service container. A variable reaches an agent only when the agent lists the name under **Allowed environment names**. The example holds no secrets; prefer importing secrets in the dashboard over putting API keys in `.env`.

**Trust model.** When local execution is enabled in Settings, agents and their tools run as the service user inside the container. They can read and write everything that user can, including `/data`, `/work` and the protected `/data/secrets` directory. Secret encryption protects stored values at rest and in backups, not from agents you allow to execute locally. Enable local execution only for agents you trust with the whole workspace, or keep it off so agents can use only the conversation tools.

`arguebot-data` holds the application home, database, metadata and logs; `arguebot-keys` holds the secret encryption keys, the session key (deleting `session.key` signs everyone out) and the token key that lets tokens be revealed; `arguebot-work` holds files used by agents. Keep these volumes together across updates. The process runs as an unprivileged user. Bind mounts, if substituted, must be writable by UID 10001. Do not mount a host login directory as a writable shared home for multiple agent processes.

## Configure models and imported login secrets

Use the Participants page tabs to import secrets, define endpoints and models, and create the agents that debate. See [the v2 usage guide](../docs/MULTIPLE_AGENTS.md).

A container has no Claude Code or Codex login of its own, so every Claude Code or Codex agent needs a stored secret.

- Claude Code: run `claude setup-token` on a trusted machine and paste the token on the Secrets tab. Import it as `claude_oauth`, then select that secret on the Claude Code agent. The service injects `CLAUDE_CODE_OAUTH_TOKEN` only into its invocation environment.
- Codex: create a dedicated login with `mkdir -p ~/.codex-arguebot && CODEX_HOME=~/.codex-arguebot codex login`. Import the complete resulting `auth.json` as `codex_auth_json`. The service owns refresh and writes a per-invocation `CODEX_HOME` containing a sanitized secret copy with an empty `refresh_token` field, preserving Codex parser compatibility without refresh authority. Do not concurrently refresh another copy of this login. Replace the stored document when the status requires reauthentication.
- HTTP models: import a token as `token`, then bind it to the endpoint's bearer, API-key header, custom header or query-string field. Use secret references, not values embedded in a base URL or static routing header.

Secret values never return through metadata APIs. They are encrypted in the database using the protected keys in `/data/secrets`, held in the separate `arguebot-keys` named volume. Before any rotation that is `credential.key`; after a rotation it is the keyring `credential.keys.json`. Back up the whole directory. Losing it makes existing encrypted secrets unusable, and the service refuses to start if rotated secrets exist without their keyring. Do not copy only the database when moving an installation. Refresh rejection, interruption or an uncertain refresh response requires reimport; the service does not repeatedly replay an old refresh token.

A host Ollama listener is reached from the container through `http://host.docker.internal:11434`, subject to the host's listener/firewall configuration. The endpoint must be reachable from the service, not just from the browser. An HTTP service in another Compose network should use its service DNS name. Remote credentialed gateways require HTTPS.

## Other surfaces

Attach the terminal or CLI to the same container-owned service:

```sh
docker compose -f docker/compose.yaml exec arguebot dotnet /app/tui/ArgueBot.Tui.dll --home /data
docker compose -f docker/compose.yaml exec arguebot dotnet /app/cli/ArgueBot.Cli.dll --home /data capabilities
```

Use stdin or a local file for secret JSON; avoid putting tokens into shell arguments. The web import reads a selected file locally and sends its content to the service. All participants execute inside the container; use `/work` for their working directory. Local files and programs are available according to the service's execution setting.

## Update and recovery

Run `docker/update.sh` on macOS/Linux or `docker\update.bat` on Windows. These scripts pull the published image, recreate the service and retain its volumes. There is no published application image to pull yet; `build --pull` updates base images. They never run `down --volumes`.

The health check polls `/health` every ten seconds once the service has started, and every two seconds during a 30-second start period, so a slow first start or schema upgrade is not reported unhealthy. Three consecutive failures mark it unhealthy. The optional Ollama service uses the same timing; its check runs `ollama list`, which calls the Ollama HTTP API, because the Ollama image does not include curl. Check `docker compose -f docker/compose.yaml logs --tail 100` for startup errors. The provider CLIs and model endpoints have separate health and authentication requirements; service health alone does not prove a provider login works. A service restart preserves interrupted attempts and requires explicit moderator recovery. Never automatically resume uncertain agent work as part of deployment.

If image creation reports package signature errors with a full Docker virtual disk, expand or free Docker storage before retrying. Do not prune unrelated images or volumes as an application update step. Existing encrypted secrets and debates must survive a container replacement and an internal schema upgrade before a release is accepted.

## Optional local Ollama service

Run `docker compose -f docker/compose.yaml --profile ollama up -d --build --wait` to include the optional provider. Its image is pinned to [Ollama 0.40.13](https://github.com/ollama/ollama/releases/tag/v0.40.13); its model storage is separate in `ollama-models`. It publishes no host port. Configure an ArgueBot endpoint with protocol `ollama` and base URL `http://ollama:11434`, then use **Check connection / list models**.

No model is downloaded automatically. Choose a model suited to the available memory and explicitly pull it with `docker compose -f docker/compose.yaml --profile ollama exec ollama ollama pull MODEL_ID`. This default profile uses CPU execution. GPU device configuration is host-specific and is not part of the verified container evidence. External providers continue to work without enabling this profile.

The application data, encryption key and execution workspace use separate named volumes. If migrating an earlier preview that kept `secrets/` inside its data volume, transfer the original key into `arguebot-keys` before starting this Compose layout. A database with existing secrets and a missing key refuses startup; it never silently generates a replacement key. Preserve all three application volumes during updates.

After building, run `node scripts/test-container.mjs` for an isolated acceptance check. It uses a unique Compose project and loopback port, then deletes only its own test volumes. The fixture exercises four-seat consensus, packaged executables, write-only imports, Claude environment injection, private Codex files, master-key rotation, container replacement and missing-key recovery. Provider responses and secrets are fictional; passing does not certify a live provider login. Evidence and current platform coverage are recorded in the original implementation plan.

For an alternate published port, set `ARGUEBOT_PORT` in `docker/.env` and use the same port in the browser URL; Compose keeps the `127.0.0.1` binding. The service validates the incoming loopback Host header and its matching browser origin, independently of the container's `0.0.0.0` listener. CLI agents receive only standard process settings and the environment names they explicitly allow. Add service-host variables to the agent's Allowed environment names when using environment-based provider authentication.

Secret master-key rotation is available through the web/TUI secret configuration and `secret-keys rotate EXPECTED_KEY_ID`. Preserve the entire protected secrets directory, including `credential.keys.json` after rotation, separately from database backups. Previous keys are retained for recovery and old backups until you retire them with **Retire earlier keys** or `secret-keys retire EXPECTED_KEY_ID`; after that, older database copies cannot decrypt their secrets. Key rotation does not change secret values or their revisions. See [the multi-agent guide](../docs/MULTIPLE_AGENTS.md).
