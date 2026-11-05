#!/bin/sh
set -eu
cd "$(dirname "$0")"
docker compose build --pull
docker compose down
docker compose up -d --wait
docker ps -a
