#!/usr/bin/env bash
# Run ArgueBot interactively in this terminal.
# Usage: ./go-terminal.sh [options]   (see ./go-terminal.sh --help)
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

exec npm start -- "$@"
