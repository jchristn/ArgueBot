#!/usr/bin/env bash
# Build the C# dashboard, then start or attach to its service. Forward --home and --port.
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDK is required to build ArgueBot." >&2
  exit 1
fi
if ! command -v npm >/dev/null 2>&1; then
  echo "Node.js and npm are required to build the web dashboard." >&2
  exit 1
fi
if [ ! -d dashboard/node_modules ]; then
  npm --prefix dashboard ci
fi
npm --prefix dashboard run build
dotnet build src/ArgueBot.sln -c Release
exec dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll service web "$@"
