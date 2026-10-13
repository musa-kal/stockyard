# 0005. The reconcile corrects the projection inside the procedure

Status: accepted

## Context

`StockOnHand` is a projection maintained from events; `StockMovement` is the truth. The two can
drift: a bug in the handler, a manual fix in the database, a message that sat in the DLQ past
its retention. The nightly reconcile has to find drift and fix it.

Two shapes were possible. The exporter could read the differences, publish them, and let the
projections worker apply corrections like any other event. Or the reconcile could correct the
rows itself and announce what it did.

Correcting through events adds a round trip through the queue for something that is a single
statement in SQL Server, and the correction would race with the ordinary events in the same
queue. A naive in-place correction has its own race: if the reconcile sets a pair to the ledger
total while a `DeliveryReceived` for that pair is still on its way, the projection applies the
delivery again on top of the corrected value and counts it twice.

## Decision

`usp_ReconcileStock` corrects `StockOnHand` in its own transaction and returns the rows that
differed. When anything differed it also writes a `StockReconciled` outbox row, which is
informational only: by the time anyone reads it, the projection is already right.

It skips item and site pairs that have an event the projections consumer has not applied yet: a
`DeliveryReceived` or `StockIssued` outbox row from the last 14 days without a `ProcessedMessage`
row for the `projections` consumer. Those pairs are behind, not wrong. Fourteen days is the
longest SQS can keep a message, so anything older can no longer arrive.

With `@Apply = 1` (the default, used by the exporter) the procedure runs `SERIALIZABLE` and reads
the ledger first, then the outbox and guard rows, then the projection, the same order in which
writers take their locks, so concurrent writers wait instead of deadlocking. With `@Apply = 0` it
runs read committed, holds nothing and writes nothing; `GET /api/v1/stock/reconcile` uses that
mode and adds the pending message and unpublished outbox counts.

## Consequences

- One statement-level correction per run, no extra messages, and no double counting of events in
  flight. `ReconcileStockTests` covers reporting, correcting, creating missing rows, zeroing stale
  ones and leaving in-flight pairs alone.
- The nightly run blocks new movements and projection updates for the length of its
  transaction. On the one million movement perf ledger (2,000 items at 21 places, see
  [performance](../performance.md)) a run that corrected all 42,000 pairs took 986 ms and a run
  with nothing to correct about 350 ms, and the run is at 02:00 UTC.
- The procedure knows the consumer name `projections`. That name is a constant in
  `StockProjectionHandler`, whose comment names the procedure.
- `StockReconciled` is consumed by nobody yet; it is there for the warehouse layer and for alerting
  on repeated drift.
