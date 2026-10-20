#!/usr/bin/env bash
# Start the ArgueBot web UI on a localhost listener.
# Usage: ./go-web-legacy.sh [--port N]
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm was not found. Install Node.js 18+ from https://nodejs.org/" >&2
  exit 1
fi

if [ ! -d node_modules ]; then
  echo "Installing dependencies..."
  npm install
fi

exec npm run web -- --host 127.0.0.1 "$@"
