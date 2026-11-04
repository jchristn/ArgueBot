FROM node:24-bookworm-slim AS dashboard
WORKDIR /source/dashboard
COPY dashboard/package*.json ./
RUN npm ci
COPY assets /source/assets
COPY icon.png /source/icon.png
COPY dashboard ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props NuGet.Config ./
COPY assets ./assets
COPY src ./src
COPY --from=dashboard /source/dashboard/dist ./dashboard/dist
RUN dotnet restore src/ArgueBot.Server --locked-mode && dotnet restore src/ArgueBot.Cli --locked-mode && dotnet restore src/ArgueBot.Tui --locked-mode
RUN dotnet publish src/ArgueBot.Server -c Release -f net10.0 --no-restore -o /publish/server && dotnet publish src/ArgueBot.Cli -c Release -f net10.0 --no-restore -o /publish/cli && dotnet publish src/ArgueBot.Tui -c Release -f net10.0 --no-restore -o /publish/tui

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
USER root
RUN sed -i 's|http://|https://|g' /etc/apt/sources.list.d/ubuntu.sources && apt-get update && apt-get install -y --no-install-recommends curl ca-certificates git && rm -rf /var/lib/apt/lists/*
COPY --from=dashboard /usr/local/bin/node /usr/local/bin/node
COPY --from=dashboard /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm
ARG CLAUDE_CODE_VERSION=2.1.317
ARG CODEX_VERSION=0.166.0
ARG GEMINI_CLI_VERSION=0.66.0
RUN npm install --global @anthropic-ai/claude-code@${CLAUDE_CODE_VERSION} @openai/codex@${CODEX_VERSION} @google/gemini-cli@${GEMINI_CLI_VERSION} && npm cache clean --force
RUN groupadd --gid 10001 arguebot && useradd --uid 10001 --gid 10001 --create-home arguebot && mkdir -p /data/secrets /work && chown arguebot:arguebot /data /data/secrets /work && chmod 700 /data/secrets
COPY --from=build /publish /app
COPY docker/app/entrypoint.mjs /app/entrypoint.mjs
ENV ARGUEBOT_HOME=/data
WORKDIR /work
USER arguebot
EXPOSE 4317
HEALTHCHECK --interval=10s --timeout=5s --retries=3 --start-period=30s --start-interval=2s CMD curl --fail --silent --max-time 4 http://127.0.0.1:4317/health || exit 1
ENTRYPOINT ["node", "/app/entrypoint.mjs"]
