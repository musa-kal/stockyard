#!/usr/bin/env pwsh
# Loads the sample master data (fictional suppliers, job sites and items) through the api.
# Rows that already exist answer 409 and are left alone, so the script can run again.
[CmdletBinding()]
param([string] $Api = 'http://localhost:8080')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Post([string] $path, [hashtable] $body) {
    $json = $body | ConvertTo-Json -Compress
    Invoke-RestMethod -Method Post -Uri "$Api$path" -Body $json -ContentType 'application/json' `
        -SkipHttpErrorCheck -StatusCodeVariable status | Out-Null
    switch ($status) {
        201 { Write-Host "created  $path $json" }
        409 { Write-Host "exists   $path $json" }
        default { throw "failed ($status) $path $json" }
    }
}

# Right after "docker compose up" the api may still be waiting for the migration.
foreach ($attempt in 1..120) {
    $ready = try { (Invoke-WebRequest "$Api/readyz" -SkipHttpErrorCheck).StatusCode -eq 200 } catch { $false }
    if ($ready) { break }
    Start-Sleep -Milliseconds 500
}

Post '/api/v1/suppliers' @{ code = 'NGPS'; name = 'Northgate Pipe Supply'; webhookUrl = 'http://fake-supplier:8090/webhooks' }
Post '/api/v1/suppliers' @{ code = 'HLV'; name = 'Harbourline Valves'; webhookUrl = 'http://fake-supplier:8090/webhooks' }
Post '/api/v1/suppliers' @{ code = 'KCF'; name = 'Keel and Co. Fittings' }

Post '/api/v1/job-sites' @{ code = 'RIVERSIDE'; name = 'Riverside Clinic' }
Post '/api/v1/job-sites' @{ code = 'HARBOUR-ST'; name = 'Harbour Street Tower' }
Post '/api/v1/job-sites' @{ code = 'LAKEVIEW'; name = 'Lakeview School' }

Post '/api/v1/items' @{ sku = 'PIPE-CU-15'; description = 'Copper pipe 15 mm, 3 m length'; unitOfMeasure = 'LEN'; unitCost = 21.40; reorderPoint = 40 }
Post '/api/v1/items' @{ sku = 'PIPE-CU-22'; description = 'Copper pipe 22 mm, 3 m length'; unitOfMeasure = 'LEN'; unitCost = 34.10; reorderPoint = 30 }
Post '/api/v1/items' @{ sku = 'ELBOW-CU-22'; description = 'Copper elbow 22 mm'; unitOfMeasure = 'EA'; unitCost = 3.45; reorderPoint = 100 }
Post '/api/v1/items' @{ sku = 'TEE-CU-22'; description = 'Copper equal tee 22 mm'; unitOfMeasure = 'EA'; unitCost = 4.95; reorderPoint = 60 }
Post '/api/v1/items' @{ sku = 'VALVE-BV-25'; description = 'Ball valve 25 mm, full bore'; unitOfMeasure = 'EA'; unitCost = 18.75; reorderPoint = 12 }
Post '/api/v1/items' @{ sku = 'VALVE-CHK-25'; description = 'Swing check valve 25 mm'; unitOfMeasure = 'EA'; unitCost = 26.30; reorderPoint = 8 }
