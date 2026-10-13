# Load tests

Two k6 scripts, run with the `grafana/k6` image so nothing is installed locally.

| Script | What it drives | Load shape | Pass thresholds |
| ----- | ----- | ----- | ----- |
| `post-order.js` | `POST /purchase-orders`, `POST /purchase-orders/{id}/lines`, `POST /purchase-orders/{id}/post` with a unique `Idempotency-Key` | ramp to 10 VUs over 30 s, hold 1 min, ramp down | errors under 1 %, checks over 99 %, p95 create and add line under 300 ms, p95 post under 500 ms, p99 post under 1 s |
| `stock-on-hand.js` | `GET /stock/on-hand?itemSku=&jobSiteCode=` (reads `dbo.vw_StockOnHand`) | constant 50 requests per second for 1 min (`RATE` overrides) | errors under 1 %, checks over 99 %, p95 under 200 ms, p99 under 400 ms, no dropped iterations |

Both scripts create their own supplier, item, and job site in `setup()` with codes derived from the run start time, so repeated runs do not collide and every code stays inside the API limits (16 characters for codes, 32 for SKUs). The request bodies use the API's field names exactly: `supplierCode`, `jobSiteCode`, `itemSku`, `quantityOrdered`, `unitCost`, and so on.

The thresholds are the targets a run must meet, not results. No run against a deployed stack has been recorded in this repository yet.

## Against the compose stack

Start the stack (see the README at the repository root), then from the repository root:

```
docker run --rm -i -e BASE_URL=http://host.docker.internal:8080 grafana/k6 run - < perf/k6/post-order.js
docker run --rm -i -e BASE_URL=http://host.docker.internal:8080 grafana/k6 run - < perf/k6/stock-on-hand.js
```

Run one script at a time and stop other containers you do not need; SQL Server, LocalStack, the services, and k6 together are a lot for a laptop, and a machine that swaps measures its own disk, not the API.

## Against EKS

The api Service is internal (ClusterIP), so forward a local port to it first:

```
kubectl port-forward --namespace stockyard service/stockyard-api 8080:80
docker run --rm -i -e BASE_URL=http://host.docker.internal:8080 grafana/k6 run - < perf/k6/post-order.js
```

Port forwarding sends every request through the API server and one pod, so the numbers describe that path rather than the cluster's capacity. For a capacity test, run k6 from a pod in the cluster against `http://stockyard-api.stockyard.svc.cluster.local`.

## Checking a script without running it

```
docker run --rm -i grafana/k6 inspect - < perf/k6/post-order.js
docker run --rm -i grafana/k6 inspect - < perf/k6/stock-on-hand.js
```
