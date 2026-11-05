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
  echo "A .NET SDK (8 or newer, able to target $framework) is required to build ArgueBot." >&2
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
# Build only the selected framework with any installed SDK that can target it. The repository's
# pinned SDK applies to verification and packaging, so build from a directory with its own global.json.
root="$(pwd)"
launcher="$root/.run/launcher"
mkdir -p "$launcher"
printf '{"sdk":{"version":"8.0.100","rollForward":"latestMajor"}}\n' > "$launcher/global.json"
build() {
  (cd "$launcher" && dotnet build "$root/src/$1/$1.csproj" -c Release -p:TargetFrameworks="$framework" -p:NuGetLockFilePath="obj/launcher.$framework.lock.json")
}
build ArgueBot.Server
build ArgueBot.Cli
# Run on a newer runtime when the selected one is not installed.
export DOTNET_ROLL_FORWARD=Major
exec dotnet "src/ArgueBot.Cli/bin/Release/$framework/ArgueBot.Cli.dll" service web "$@"
