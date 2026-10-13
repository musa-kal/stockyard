#!/usr/bin/env pwsh
# End-to-end smoke test against the docker compose stack (PowerShell 7).
#   docker compose up --build -d
#   pwsh ./scripts/smoke.ps1
# Creates its own supplier, items and job site, so it can run any number of times.
[CmdletBinding()]
param(
    [string] $Api = 'http://localhost:8080',
    [string] $FakeSupplier = 'http://localhost:8090',
    [int] $TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Fail([string] $message) { Write-Host "SMOKE FAILED: $message" -ForegroundColor Red; exit 1 }

function Call([string] $method, [string] $path, $body = $null, [hashtable] $headers = @{}, [int] $expect = 0) {
    $parameters = @{
        Method = $method
        Uri = "$Api$path"
        Headers = $headers
        SkipHttpErrorCheck = $true
        StatusCodeVariable = 'status'
    }
    if ($null -ne $body) {
        $parameters.Body = ($body | ConvertTo-Json -Depth 10)
        $parameters.ContentType = 'application/json'
    }

    $response = Invoke-RestMethod @parameters
    if ($expect -ne 0 -and $status -ne $expect) {
        Fail "$method $path returned $status, expected $expect`n$($response | ConvertTo-Json -Depth 10)"
    }

    return $response
}

function WaitFor([string] $what, [scriptblock] $condition) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 500
    }

    Fail "timed out after $TimeoutSeconds s waiting for $what"
}

Step "waiting for $Api/readyz"
WaitFor 'the api to be ready' {
    try { (Invoke-WebRequest "$Api/readyz" -SkipHttpErrorCheck).StatusCode -eq 200 } catch { $false }
} | Out-Null

$run = [DateTime]::UtcNow.ToString('MMddHHmmss')
$supplier = "SMK$run"
$site = "SITE$run"
$elbow = "SMK-$run-ELBOW"
$valve = "SMK-$run-VALVE"

Step "creating supplier $supplier, items $elbow and $valve, job site $site"
Call POST '/api/v1/suppliers' @{ code = $supplier; name = 'Northgate Pipe Supply'; webhookUrl = 'http://fake-supplier:8090/webhooks' } -expect 201 | Out-Null
Call POST '/api/v1/items' @{ sku = $elbow; description = 'Copper elbow 22 mm'; unitOfMeasure = 'EA'; unitCost = 3.45; reorderPoint = 50 } -expect 201 | Out-Null
Call POST '/api/v1/items' @{ sku = $valve; description = 'Ball valve 25 mm'; unitOfMeasure = 'EA'; unitCost = 18.75; reorderPoint = 5 } -expect 201 | Out-Null
Call POST '/api/v1/job-sites' @{ code = $site; name = 'Harbour Street Tower' } -expect 201 | Out-Null

Step 'raising a purchase order with two lines'
$order = Call POST '/api/v1/purchase-orders' @{ supplierCode = $supplier; jobSiteCode = $site } -expect 201
$orderId = $order.id
Call POST "/api/v1/purchase-orders/$orderId/lines" @{ itemSku = $elbow; quantityOrdered = 40 } -expect 201 | Out-Null
Call POST "/api/v1/purchase-orders/$orderId/lines" @{ itemSku = $valve; quantityOrdered = 6 } -expect 201 | Out-Null

Step "posting $($order.number) twice with the same Idempotency-Key"
$key = [guid]::NewGuid().ToString()
$first = Call POST "/api/v1/purchase-orders/$orderId/post" -headers @{ 'Idempotency-Key' = $key } -expect 200
$second = Call POST "/api/v1/purchase-orders/$orderId/post" -headers @{ 'Idempotency-Key' = $key } -expect 200
if ($first.status -ne 'Posted') { Fail "first post returned status $($first.status)" }
if (($first | ConvertTo-Json -Depth 10) -ne ($second | ConvertTo-Json -Depth 10)) { Fail 'the second post returned a different order' }
$posted = @(Call GET '/api/v1/purchase-orders?status=Posted' -expect 200 | Where-Object { $_.id -eq $orderId })
if ($posted.Count -ne 1 -or $posted[0].lines.Count -ne 2) { Fail 'the order is not posted exactly once with its two lines' }
Write-Host "    same order back: $($second.number), status $($second.status), postedAt $($second.postedAt)"

Step 'receiving the full delivery'
$received = Call POST "/api/v1/purchase-orders/$orderId/receipts" @{ lines = @(@{ lineNumber = 1; quantity = 40 }, @{ lineNumber = 2; quantity = 6 }) } -expect 200
if ($received.status -ne 'Received') { Fail "status after the receipt is $($received.status)" }

Step "issuing 12 x $elbow at $site"
$issue = Call POST '/api/v1/stock/issues' @{ itemSku = $elbow; jobSiteCode = $site; quantity = 12; reference = 'WO-SMOKE' } -expect 201
$onHand = @(Call GET "/api/v1/stock/on-hand?itemSku=$elbow&jobSiteCode=$site" -expect 200)
if ($onHand[0].quantity -ne 28) { Fail "on hand is $($onHand[0].quantity), expected 28" }
Write-Host "    movement $($issue.id); on hand now $($onHand[0].quantity)"

Step 'waiting for the projection to catch up'
$report = WaitFor 'the outbox and the queue to drain' {
    $r = Call GET '/api/v1/stock/reconcile' -expect 200
    if ($r.pendingMessages -eq 0 -and $r.unpublishedOutboxMessages -eq 0) { $r } else { $null }
}
Write-Host "    GET /api/v1/stock/reconcile: $($report | ConvertTo-Json -Depth 10 -Compress)"
if (@($report.differences).Count -ne 0) { Fail 'the projection differs from the ledger' }

Step 'checking the fake supplier received two signed webhooks for the order'
$webhooks = WaitFor 'two webhooks' {
    $all = Invoke-RestMethod "$FakeSupplier/received"
    $mine = @($all | Where-Object { $_.payload -and "$($_.payload.orderId)" -eq "$orderId" })
    if ($mine.Count -ge 2) { , $mine } else { $null }
}
foreach ($hook in $webhooks) { Write-Host "    $($hook.eventType) delivery $($hook.deliveryId) signatureValid=$($hook.signatureValid)" }
if ($webhooks.Count -ne 2) { Fail "expected 2 webhooks for the order, got $($webhooks.Count)" }
if (@($webhooks | Where-Object { -not $_.signatureValid }).Count -ne 0) { Fail 'a webhook signature did not verify' }
$types = @($webhooks.eventType | Sort-Object)
if (($types -join ',') -ne 'DeliveryReceived,PurchaseOrderPosted') { Fail "unexpected webhook types: $($types -join ', ')" }

Step 'running an export for today'
$today = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
$export = Call POST "/api/v1/exports/run?date=$today" -expect 200
foreach ($file in $export.files) { Write-Host "    wrote $file" }
if ($export.orderLines -lt 2 -or $export.movements -lt 3) { Fail "export counted $($export.orderLines) order lines and $($export.movements) movements" }

Write-Host 'SMOKE PASSED' -ForegroundColor Green
