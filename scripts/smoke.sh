#!/usr/bin/env bash
# End-to-end smoke test against the docker compose stack. Needs curl and jq.
#   docker compose up --build -d
#   bash ./scripts/smoke.sh
# Creates its own supplier, items and job site, so it can run any number of times.
set -euo pipefail

API="${API:-http://localhost:8080}"
FAKE_SUPPLIER="${FAKE_SUPPLIER:-http://localhost:8090}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-120}"

step() { printf '==> %s\n' "$*"; }
fail() { printf 'SMOKE FAILED: %s\n' "$*" >&2; exit 1; }

# call METHOD PATH EXPECTED_STATUS [BODY] [extra curl args...]; prints the response body.
call() {
  local method=$1 path=$2 expect=$3 body=${4:-}
  shift 3; [ $# -gt 0 ] && shift
  local out status
  out=$(mktemp)
  if [ -n "$body" ]; then
    status=$(curl -sS -o "$out" -w '%{http_code}' -X "$method" "$API$path" -H 'Content-Type: application/json' --data "$body" "$@")
  else
    status=$(curl -sS -o "$out" -w '%{http_code}' -X "$method" "$API$path" "$@")
  fi
  if [ "$status" != "$expect" ]; then
    cat "$out" >&2; rm -f "$out"
    fail "$method $path returned $status, expected $expect"
  fi
  cat "$out"; rm -f "$out"
}

# wait_for DESCRIPTION COMMAND...; retries the command until it succeeds or the timeout passes.
wait_for() {
  local what=$1; shift
  local deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
  until "$@"; do
    [ "$(date +%s)" -lt "$deadline" ] || fail "timed out after ${TIMEOUT_SECONDS} s waiting for $what"
    sleep 0.5
  done
}

ready() { [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/readyz")" = "200" ]; }

step "waiting for $API/readyz"
wait_for "the api to be ready" ready

run=$(date -u +%m%d%H%M%S)
supplier="SMK$run"
site="SITE$run"
elbow="SMK-$run-ELBOW"
valve="SMK-$run-VALVE"

step "creating supplier $supplier, items $elbow and $valve, job site $site"
call POST /api/v1/suppliers 201 "{\"code\":\"$supplier\",\"name\":\"Harbourline Valves\",\"webhookUrl\":\"http://fake-supplier:8090/webhooks\"}" > /dev/null
call POST /api/v1/items 201 "{\"sku\":\"$elbow\",\"description\":\"Copper elbow 22 mm\",\"unitOfMeasure\":\"EA\",\"unitCost\":3.45,\"reorderPoint\":50}" > /dev/null
call POST /api/v1/items 201 "{\"sku\":\"$valve\",\"description\":\"Ball valve 25 mm\",\"unitOfMeasure\":\"EA\",\"unitCost\":18.75,\"reorderPoint\":5}" > /dev/null
call POST /api/v1/job-sites 201 "{\"code\":\"$site\",\"name\":\"Lakeview School\"}" > /dev/null

step "raising a purchase order with two lines"
order=$(call POST /api/v1/purchase-orders 201 "{\"supplierCode\":\"$supplier\",\"jobSiteCode\":\"$site\"}")
order_id=$(jq -r .id <<< "$order")
number=$(jq -r .number <<< "$order")
call POST "/api/v1/purchase-orders/$order_id/lines" 201 "{\"itemSku\":\"$elbow\",\"quantityOrdered\":40}" > /dev/null
call POST "/api/v1/purchase-orders/$order_id/lines" 201 "{\"itemSku\":\"$valve\",\"quantityOrdered\":6}" > /dev/null

step "posting $number twice with the same Idempotency-Key"
key=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || date -u +%s%N)
first=$(call POST "/api/v1/purchase-orders/$order_id/post" 200 "" -H "Idempotency-Key: $key")
second=$(call POST "/api/v1/purchase-orders/$order_id/post" 200 "" -H "Idempotency-Key: $key")
[ "$(jq -r .status <<< "$first")" = "Posted" ] || fail "first post returned status $(jq -r .status <<< "$first")"
[ "$(jq -S . <<< "$first")" = "$(jq -S . <<< "$second")" ] || fail "the second post returned a different order"
posted=$(call GET "/api/v1/purchase-orders?status=Posted" 200 | jq --arg id "$order_id" '[.[] | select(.id == $id)]')
[ "$(jq 'length' <<< "$posted")" = "1" ] || fail "the order is not posted exactly once"
[ "$(jq '.[0].lines | length' <<< "$posted")" = "2" ] || fail "the posted order does not have its two lines"
echo "    same order back: $(jq -r '.number + ", status " + .status + ", postedAt " + .postedAt' <<< "$second")"

step "receiving the full delivery"
received=$(call POST "/api/v1/purchase-orders/$order_id/receipts" 200 '{"lines":[{"lineNumber":1,"quantity":40},{"lineNumber":2,"quantity":6}]}')
[ "$(jq -r .status <<< "$received")" = "Received" ] || fail "status after the receipt is $(jq -r .status <<< "$received")"

step "issuing 12 x $elbow at $site"
issue=$(call POST /api/v1/stock/issues 201 "{\"itemSku\":\"$elbow\",\"jobSiteCode\":\"$site\",\"quantity\":12,\"reference\":\"WO-SMOKE\"}")
on_hand=$(call GET "/api/v1/stock/on-hand?itemSku=$elbow&jobSiteCode=$site" 200 | jq '.[0].quantity')
[ "$(jq -n "$on_hand == 28")" = "true" ] || fail "on hand is $on_hand, expected 28"
echo "    movement $(jq -r .id <<< "$issue"); on hand now $on_hand"

step "waiting for the projection to catch up"
drained() {
  report=$(call GET /api/v1/stock/reconcile 200)
  [ "$(jq '.pendingMessages == 0 and .unpublishedOutboxMessages == 0' <<< "$report")" = "true" ]
}
wait_for "the outbox and the queue to drain" drained
echo "    GET /api/v1/stock/reconcile: $(jq -c . <<< "$report")"
[ "$(jq '.differences | length' <<< "$report")" = "0" ] || fail "the projection differs from the ledger"

step "checking the fake supplier received two signed webhooks for the order"
two_webhooks() {
  webhooks=$(curl -sS "$FAKE_SUPPLIER/received" | jq --arg id "$order_id" '[.[] | select(.payload != null and ((.payload.orderId // "") | ascii_downcase) == $id)]')
  [ "$(jq 'length' <<< "$webhooks")" -ge 2 ]
}
wait_for "two webhooks" two_webhooks
jq -r '.[] | "    \(.eventType) delivery \(.deliveryId) signatureValid=\(.signatureValid)"' <<< "$webhooks"
[ "$(jq 'length' <<< "$webhooks")" = "2" ] || fail "expected 2 webhooks for the order, got $(jq 'length' <<< "$webhooks")"
[ "$(jq 'all(.signatureValid)' <<< "$webhooks")" = "true" ] || fail "a webhook signature did not verify"
[ "$(jq -r '[.[].eventType] | sort | join(",")' <<< "$webhooks")" = "DeliveryReceived,PurchaseOrderPosted" ] || fail "unexpected webhook types"

step "running an export for today"
export_result=$(call POST "/api/v1/exports/run?date=$(date -u +%Y-%m-%d)" 200)
jq -r '.files[] | "    wrote \(.)"' <<< "$export_result"
[ "$(jq '.orderLines >= 2 and .movements >= 3' <<< "$export_result")" = "true" ] || fail "the export missed rows: $(jq -c . <<< "$export_result")"

echo "SMOKE PASSED"
