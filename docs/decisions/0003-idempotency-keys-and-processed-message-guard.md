# 0003. Idempotency keys on posting and a ProcessedMessage guard in the consumer

Status: accepted

## Context

Duplicates arrive from both ends.

- Clients retry. A site office on a poor connection that does not see the response to
  `POST /purchase-orders/{id}/post` will send it again, and the order must not be posted twice or
  produce a second `PurchaseOrderPosted` event and webhook.
- SQS standard queues deliver at least once, and the outbox relay can send a row twice
  ([ADR 0001](0001-outbox-over-dual-writes.md)). The projections worker adds and subtracts
  quantities, so applying a message twice is a wrong stock figure, not a harmless repeat.

## Decision

Posting requires an `Idempotency-Key` header of 1 to 64 characters; without one the api answers
400. `usp_PostPurchaseOrder` stores the key on the order under a unique filtered index. Posting
again with the same key returns the same order and changes nothing; a different key on an order
that is already posted raises 50001 (409).

The consumer inserts a `ProcessedMessage (ConsumerName, MessageId)` row and applies the change in
one transaction. `MessageId` is the outbox row id, the same on every delivery of the event.

- A duplicate fails the guard's primary key; the transaction rolls back and the message is
  deleted from the queue.
- A crash after the guard insert and before the commit rolls back both, so the redelivery applies
  the change once.
- Two deliveries processed at the same moment serialise on the guard's key lock; the second one
  sees the committed row and takes the duplicate path.

## Consequences

- The projection is exactly once on top of at-least-once delivery.
  `ExactlyOnceConsumptionTests` covers the same message delivered twice, four concurrent
  deliveries, and an interruption between the guard insert and the commit followed by a
  redelivery.
- Event types the projection ignores still get a guard row, so `ProcessedMessage` records every
  message the consumer saw.
- `ProcessedMessage` grows with the event stream. Rows older than the queue's 14 day retention can
  be deleted safely, since nothing older can be delivered again.
- The reconcile uses the guard rows to tell an event that is still in flight from real drift
  ([ADR 0005](0005-reconcile-corrects-in-the-procedure.md)).
