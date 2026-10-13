# Performance: the stock on hand read

`GET /api/v1/stock/on-hand?itemSku=` is the read the site office uses all day: how much of one
item is where. It does not read the `StockOnHand` projection; it reads `dbo.vw_StockOnHand`, which
sums the movement ledger, so its answer is always current. The cost of that choice is a query over
a table that only grows. This page measures it for one item on a one million row ledger in three
index states and explains which one the migrations ship.

Everything below was measured locally. Nothing here was measured on AWS; see
[what is still pending](#pending-a-deployment).

## Machine and method

| | |
| --- | --- |
| Machine | Windows 11 laptop, 16 GB RAM, Docker Desktop on WSL 2 |
| Docker VM | 20 logical CPUs and 11.7 GiB visible to the engine |
| Database | SQL Server 2022 Developer (16.0.4305.5, CU27 GDR) in the Linux container from `docker-compose.yml`, default settings (MAXDOP 0) |
| Other load | the rest of the compose stack stopped; only `sqlserver` running |
| Data | `scripts/perf/seed-movements.sql` in a separate `StockyardPerf` database: 2,000 items, 20 job sites plus the warehouse, 1,000,000 movements. Each item has 500 movements interleaved through the table (row r belongs to item r % 2000), spread over 21 places, every fourth one an issue. |
| Query | the api's query: `SELECT ItemSku, JobSiteCode, Quantity, LastMovementAt FROM dbo.vw_StockOnHand WHERE ItemSku = @sku`, sent through `sp_executesql` with an `nvarchar(32)` parameter, for `PERF-01000` (21 result rows over 500 movements) |
| Per stage | set the indexes, `UPDATE STATISTICS ... WITH FULLSCAN`, clear the plan cache, run the query once unmeasured to warm the buffer pool and the plan, then one run with `SET STATISTICS IO, TIME, PROFILE ON` and a 200-query loop timed inside SQL Server with `SYSDATETIME()` (no network or client in the number) |
| How to repeat | `pwsh ./scripts/perf/measure.ps1` or `bash ./scripts/perf/measure.sh` with the compose stack's `.env` in place |

Two complete runs were taken, the first with `measure.ps1` and the second with `measure.sh`.
Physical reads were zero in every measured query, so these are warm-cache numbers.

## Results

| Stage | Index on `StockMovement` | Logical reads (StockMovement) | CPU, one query | Elapsed, one query | 200 queries, total | Average per query |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | none leading with `ItemId` | 14,594 / 14,594 | 403 ms / 581 ms | 63 ms / 71 ms | 7,127 ms / 7,260 ms | 35.6 ms / 36.3 ms |
| 2 | `IX_StockMovement_ItemId (ItemId)`, the index EF Core creates for the foreign key | 1,546 / 1,546 | 4 ms / 4 ms | 2 ms / 3 ms | 211 ms / 213 ms | 1.06 ms / 1.06 ms |
| 3 | `IX_StockMovement_ItemId_OccurredAt (ItemId, OccurredAt) INCLUDE (Quantity, JobSiteId)` | 8 / 8 | 0 ms / 4 ms | 1 ms / 2 ms | 71 ms / 84 ms | 0.35 ms / 0.42 ms |

Each cell is run 1 / run 2. Single-query CPU and elapsed times under 5 ms are at the resolution of
`STATISTICS TIME`; the 200-query averages are the better comparison at that end.

## The plans

Actual rows and executions from `STATISTICS PROFILE`, trimmed to the operators that matter.

Stage 1, no index on `ItemId`. The optimizer finds the item by `IX_Item_Sku`, but has no way to
find its movements except reading all of them. It goes parallel (20 threads), scans the
clustered index, and uses a bitmap built from the one item to drop all but about 3,000 of the
million rows before the aggregate:

```
Parallelism (Gather Streams)                                   rows 21
  Sort (JobSite.Code)
    Hash Match (Inner Join, builds bitmap Opt_Bitmap)
      Index Seek  Item.IX_Item_Sku                             rows 1
      Adaptive Join
        Hash Match (Aggregate on ItemId, site)
          Filter (PROBE bitmap)                                rows 3,000
            Clustered Index Scan  StockMovement.PK             rows 1,000,000
        Index Scan  JobSite.IX_JobSite_Code
```

Stage 2, the foreign key index. A seek finds the 500 rows, but the index holds only `ItemId` and
the clustered key, so every row costs a key lookup for `Quantity`, `JobSiteId` and `OccurredAt`.
Because the item's movements are spread through the table, the lookups land on about 500
different pages, which is where the 1,546 reads come from:

```
Sort (JobSite.Code)                                            rows 21
  Nested Loops
    Hash Match (Aggregate on site)                             rows 21
      Nested Loops (prefetch)                                  rows 500
        Nested Loops
          Index Seek  Item.IX_Item_Sku                         rows 1
          Index Seek  StockMovement.IX_StockMovement_ItemId    rows 500
        Clustered Index Seek  StockMovement.PK (Lookup)        rows 500, executions 500
    Clustered Index Seek  JobSite.PK                           executions 21
```

Stage 3, the covering index. The seek returns everything the view needs, in one range of
adjacent pages, and the key lookup is gone:

```
Sort (JobSite.Code)                                            rows 21
  Nested Loops
    Hash Match (Aggregate on site)                             rows 21
      Nested Loops                                             rows 500
        Index Seek  Item.IX_Item_Sku                           rows 1
        Index Seek  StockMovement.IX_StockMovement_ItemId_OccurredAt   rows 500
    Clustered Index Seek  JobSite.PK                           executions 21
```

## What ships

The `SqlObjects` migration installs `IX_StockMovement_ItemId_OccurredAt` from
`src/Stockyard.Infrastructure/Sql/IX_StockMovement_ItemId_OccurredAt.sql` and drops
`IX_StockMovement_ItemId`. The covering index leads with `ItemId`, so it also serves the foreign key
check when an item is deleted; keeping both would maintain two indexes on every ledger insert
for no read that needs the narrow one. EF Core's model snapshot still lists the foreign key
index; that is deliberate, since the snapshot only matters for diffing future migrations, and the
SQL file's header says why the database differs.

The same index serves two other ledger reads: the balance check in `usp_IssueStock` (one item at
one site) and the reconcile.

From stage 1 to stage 3 the read goes from 14,594 logical reads to 8 and from about 36 ms to about
0.4 ms per query in the loop, a factor of roughly 90. The index's cost on writes (one more
index on an append-only table) was not measured here.

## The reconcile on the same ledger

The measure scripts end by running `usp_ReconcileStock` over the whole perf ledger, which has no
projection rows of its own, so the first apply run corrects every pair:

| Run | Pairs corrected or reported | Elapsed |
| --- | --- | --- |
| run 1, `@Apply = 1`, empty projection | 42,000 corrected | 986 ms |
| run 1, `@Apply = 0` afterwards | 0 | 345 ms |
| run 2, `@Apply = 1`, nothing left to correct | 0 | 352 ms |
| run 2, `@Apply = 0` | 0 | 352 ms |

That is the window in which the nightly reconcile holds its `SERIALIZABLE` locks
([ADR 0005](decisions/0005-reconcile-corrects-in-the-procedure.md)).

## Pending a deployment

Not measured, because they need the AWS environment from `infra/terraform/`:

- The same read on RDS SQL Server. From a machine that can reach the instance, with the
  connection string from Secrets Manager:
  1. `docker run --rm -e "ConnectionStrings__Stockyard=Server=<rds endpoint>;Database=StockyardPerf;User Id=<admin>;Password=<from Secrets Manager>;TrustServerCertificate=True" stockyard-api migrate`
  2. `sqlcmd -S <rds endpoint> -U <admin> -d StockyardPerf -i scripts/perf/seed-movements.sql`
  3. `sqlcmd -S <rds endpoint> -U <admin> -d StockyardPerf -v Stage=none Sku=PERF-01000 -i scripts/perf/measure.sql`,
     then again with `Stage=fk` and `Stage=covering`.
- End-to-end latency of `GET /api/v1/stock/on-hand` through the load balancer, with the k6 script
  `perf/k6/stock-on-hand.js` run through the `grafana/k6` image as described in
  `perf/k6/README.md`.
