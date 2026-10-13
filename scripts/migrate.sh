#!/usr/bin/env bash
# Applies the EF Core migrations (tables, then the hand-written SQL objects) to the compose
# SQL Server by running the api image with "migrate". Safe to run again: applied migrations are skipped.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose up -d --wait sqlserver
docker compose run --rm --build migrate
