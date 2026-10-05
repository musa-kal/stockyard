# 0001. Publish events through an outbox, not dual writes

Status: accepted

## Context

Posting an order, receiving a delivery and issuing stock each change SQL Server and must also tell
the rest of the system: the projections worker keeps `StockOnHand`, and the warehouse layer and
suppliers care about the same facts. The obvious code writes to the database and then sends to
SQS. That is two writes with no transaction around them:

- commit, then crash before the send: the order is posted and nobody hears about it;
- send, then fail the commit: consumers act on an order that was never posted.

Retrying the send in the request does not close either gap, and SQS has no way to join a SQL
Server transaction.

## Decision

The procedures that change state insert an `OutboxMessage` row in the same transaction as the
change. The row holds the event type and its camelCase payload, built with `FOR JSON PATH` from
the rows the transaction just wrote.

A relay hosted in the api reads unpublished rows oldest first, sends each one to
`stockyard-events` with the type in the `type` message attribute, and marks it published. It
claims rows with `UPDLOCK, READPAST, ROWLOCK` inside a transaction, so several api replicas can
run the relay without stepping on each other. A failed send is recorded on the row (`Attempts`,
`LastError`) and ends the batch; the next poll tries again.

## Consequences

- An event exists if and only if its change committed.
- Delivery is at least once. A relay that crashes between the send and its commit sends the row
  again, so every consumer must deduplicate on `messageId`, which is the outbox row id
  ([ADR 0003](0003-idempotency-keys-and-processed-message-guard.md)).
- Events reach the queue up to one poll interval (1 s by default) after the commit. The read
  paths do not depend on that: `GET /stock/on-hand` reads the ledger view.
- The outbox table grows. Published rows are kept for now; they are small, the filtered index
  only covers unpublished ones, and they double as an audit trail. A retention job can delete
  rows older than the queue's 14 day maximum retention when the table gets large.
- An SQS outage does not stop the office from working; it shows up as a growing
  `unpublishedOutboxMessages` count on `GET /stock/reconcile`.
