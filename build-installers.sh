#!/bin/sh
set -eu
cd "$(dirname "$0")"
case "$(uname -s)" in
  Darwin) channels='archive pkg' ;;
  Linux) channels='archive deb rpm appimage' ;;
  *) echo 'Use build-installers.bat on Windows.' >&2; exit 2 ;;
esac
for channel in $channels; do
  dotnet run --project src/ArgueBot.Publisher -c Release -f net10.0 -- --channel "$channel" "$@"
done
