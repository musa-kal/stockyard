#!/usr/bin/env pwsh
# Measures the stock on hand read path (docs/performance.md) on the compose SQL Server.
#   pwsh ./scripts/perf/measure.ps1
# Uses its own database (StockyardPerf by default), migrated with the real migrations and
# seeded with 1,000,000 movements, then runs scripts/perf/measure.sql once per index state.
[CmdletBinding()]
param(
    [string] $Database = 'StockyardPerf',
    [string] $Sku = 'PERF-01000',
    [string[]] $Stages = @('none', 'fk', 'covering')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
Push-Location $root

function Invoke-Checked([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "command failed with exit code $LASTEXITCODE" }
}

function Invoke-SqlFile([string] $file, [string[]] $variables = @()) {
    # sqlcmd reads -v before -i; the password comes from the container's own environment.
    $setVariables = if ($variables.Count -gt 0) { '-v ' + ($variables -join ' ') } else { '' }
    $arguments = @('compose', 'exec', '-T', 'sqlserver', 'bash', '-c',
        "SQLCMDPASSWORD=`"`$MSSQL_SA_PASSWORD`" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d $Database -b -W -s '|' $setVariables -i /tmp/$file")
    Invoke-Checked { docker @arguments }
}

try {
    $password = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''
    if (-not $password) { throw 'MSSQL_SA_PASSWORD is not set in .env' }

    Write-Host "==> SQL Server and the $Database database"
    Invoke-Checked { docker compose up -d --wait sqlserver }
    $connection = "Server=sqlserver,1433;Database=$Database;User Id=sa;Password=$password;TrustServerCertificate=True"
    Invoke-Checked { docker compose run --rm --no-deps -e "ConnectionStrings__Stockyard=$connection" migrate }

    Write-Host '==> seeding 1,000,000 movements (skipped when already there)'
    Invoke-Checked { docker compose cp scripts/perf/seed-movements.sql sqlserver:/tmp/seed-movements.sql }
    Invoke-Checked { docker compose cp scripts/perf/measure.sql sqlserver:/tmp/measure.sql }
    $seedWatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-SqlFile 'seed-movements.sql'
    Write-Host ("    seed step took {0:n1} s" -f $seedWatch.Elapsed.TotalSeconds)

    foreach ($stage in $Stages) {
        Write-Host "==> stage: $stage"
        Invoke-SqlFile 'measure.sql' @("Stage=$stage", "Sku=$Sku")
    }
}
finally {
    Pop-Location
}
