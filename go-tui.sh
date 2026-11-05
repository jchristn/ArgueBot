#!/usr/bin/env bash
# Build and run the C# terminal interface for one framework, for example: ./go-tui.sh net8.0 --home .run/home
# The first argument is the target framework (net8.0 or net10.0, default net10.0). Remaining arguments are forwarded to the TUI.
set -euo pipefail
cd "$(dirname "$0")"
framework="net10.0"
if [ $# -gt 0 ] && [[ "$1" != -* ]]; then
  framework="$1"
  shift
fi
case "$framework" in
  net8.0|net10.0) ;;
  *) echo "Unsupported framework '$framework'. Use net8.0 or net10.0." >&2; exit 1 ;;
esac
if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET SDK for $framework is required to build ArgueBot." >&2
  exit 1
fi
dotnet build src/ArgueBot.Server/ArgueBot.Server.csproj -c Release -f "$framework"
dotnet build src/ArgueBot.Tui/ArgueBot.Tui.csproj -c Release -f "$framework"
exec dotnet "src/ArgueBot.Tui/bin/Release/$framework/ArgueBot.Tui.dll" "$@"
