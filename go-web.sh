#!/usr/bin/env bash
# Build the C# dashboard for one framework, then start or attach to its service, for example: ./go-web.sh net8.0 --port 4417
# The first argument is the target framework (net8.0 or net10.0, default net10.0). Remaining arguments such as --home and --port are forwarded.
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
if ! command -v npm >/dev/null 2>&1; then
  echo "Node.js and npm are required to build the web dashboard." >&2
  exit 1
fi
if [ ! -d dashboard/node_modules ]; then
  npm --prefix dashboard ci
fi
npm --prefix dashboard run build
dotnet build src/ArgueBot.Server/ArgueBot.Server.csproj -c Release -f "$framework"
dotnet build src/ArgueBot.Cli/ArgueBot.Cli.csproj -c Release -f "$framework"
exec dotnet "src/ArgueBot.Cli/bin/Release/$framework/ArgueBot.Cli.dll" service web "$@"
