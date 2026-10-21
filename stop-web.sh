#!/usr/bin/env bash
# Stop ArgueBot for the selected --home/--server.
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET is required to stop the ArgueBot service." >&2
  exit 1
fi
if [ ! -f src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll ]; then
  dotnet build src/ArgueBot.Cli -c Release
fi
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll service stop "$@"
