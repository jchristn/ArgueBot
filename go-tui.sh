#!/usr/bin/env bash
# Build and run the C# terminal interface. Arguments are forwarded to the TUI.
set -euo pipefail
cd "$(dirname "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDK is required to build ArgueBot." >&2
  exit 1
fi
dotnet build src/ArgueBot.sln -c Release
exec dotnet src/ArgueBot.Tui/bin/Release/net10.0/ArgueBot.Tui.dll "$@"
