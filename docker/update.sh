#!/bin/sh
# Pull the published ArgueBot image and recreate the stack. Volumes (data, keys, work files) are kept.
set -eu
cd "$(dirname "$0")"
docker compose pull
docker compose down
docker compose up -d --wait
docker ps -a
