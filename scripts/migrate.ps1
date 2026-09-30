#!/usr/bin/env pwsh
# Applies the EF Core migrations (tables, then the hand-written SQL objects) to the compose
# SQL Server by running the api image with "migrate". Safe to run again: applied migrations are skipped.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    docker compose up -d --wait sqlserver
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    docker compose run --rm --build migrate
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
