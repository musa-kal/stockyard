#!/usr/bin/env bash
# Loads the sample master data (fictional suppliers, job sites and items) through the api.
# Rows that already exist answer 409 and are left alone, so the script can run again.
set -euo pipefail

API="${API:-http://localhost:8080}"

post() {
  local path=$1 body=$2 status
  status=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$API$path" -H 'Content-Type: application/json' --data "$body")
  case "$status" in
    201) echo "created  $path $body" ;;
    409) echo "exists   $path $body" ;;
    *) echo "failed ($status) $path $body" >&2; exit 1 ;;
  esac
}

# Right after "docker compose up" the api may still be waiting for the migration.
for _ in $(seq 1 120); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/readyz")" = "200" ] && break
  sleep 0.5
done

post /api/v1/suppliers '{"code":"NGPS","name":"Northgate Pipe Supply","webhookUrl":"http://fake-supplier:8090/webhooks"}'
post /api/v1/suppliers '{"code":"HLV","name":"Harbourline Valves","webhookUrl":"http://fake-supplier:8090/webhooks"}'
post /api/v1/suppliers '{"code":"KCF","name":"Keel and Co. Fittings"}'

post /api/v1/job-sites '{"code":"RIVERSIDE","name":"Riverside Clinic"}'
post /api/v1/job-sites '{"code":"HARBOUR-ST","name":"Harbour Street Tower"}'
post /api/v1/job-sites '{"code":"LAKEVIEW","name":"Lakeview School"}'

post /api/v1/items '{"sku":"PIPE-CU-15","description":"Copper pipe 15 mm, 3 m length","unitOfMeasure":"LEN","unitCost":21.40,"reorderPoint":40}'
post /api/v1/items '{"sku":"PIPE-CU-22","description":"Copper pipe 22 mm, 3 m length","unitOfMeasure":"LEN","unitCost":34.10,"reorderPoint":30}'
post /api/v1/items '{"sku":"ELBOW-CU-22","description":"Copper elbow 22 mm","unitOfMeasure":"EA","unitCost":3.45,"reorderPoint":100}'
post /api/v1/items '{"sku":"TEE-CU-22","description":"Copper equal tee 22 mm","unitOfMeasure":"EA","unitCost":4.95,"reorderPoint":60}'
post /api/v1/items '{"sku":"VALVE-BV-25","description":"Ball valve 25 mm, full bore","unitOfMeasure":"EA","unitCost":18.75,"reorderPoint":12}'
post /api/v1/items '{"sku":"VALVE-CHK-25","description":"Swing check valve 25 mm","unitOfMeasure":"EA","unitCost":26.30,"reorderPoint":8}'
