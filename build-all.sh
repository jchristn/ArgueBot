#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-all.sh <tag>"
    echo "Example: build-all.sh v2.1.0"
    exit 1
fi

TAG="$1"
IMAGE="jchristn77/arguebot"

cd "$(dirname "${BASH_SOURCE[0]}")"

# Build on the cloud builder Armada uses (cloud-jchristn77-jchristn77) for both architectures and push the multi-arch manifest to Docker Hub.
echo "Building ${IMAGE}:latest and ${IMAGE}:${TAG}..."
docker buildx build \
    --builder cloud-jchristn77-jchristn77 \
    --platform linux/amd64,linux/arm64/v8 \
    -t "${IMAGE}:latest" \
    -t "${IMAGE}:${TAG}" \
    -f docker/app/Dockerfile \
    --push \
    .

# Pull the pushed tags back from Docker Hub to update the local copy.
echo "Pulling ${IMAGE}:latest..."
docker pull "${IMAGE}:latest"

echo "Pulling ${IMAGE}:${TAG}..."
docker pull "${IMAGE}:${TAG}"

echo "=== ${IMAGE}:${TAG} and :latest built, pushed to Docker Hub and pulled locally ==="
echo "Native installers are built separately with build-installers.sh; see packaging/README.md."
