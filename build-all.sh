#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-all.sh <tag>"
    echo "Example: build-all.sh v2.1.0"
    exit 1
fi

TAG="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Building ArgueBot image ==="
"${SCRIPT_DIR}/build-arguebot.sh" "$TAG"

echo "=== ArgueBot image built for ${TAG} and latest, pushed to Docker Hub, and pulled locally ==="
echo "Native installers are built separately with build-installers.sh; see packaging/README.md."
