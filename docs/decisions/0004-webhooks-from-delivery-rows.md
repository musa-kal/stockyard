# 0004. Drive supplier webhooks from delivery rows, not from the queue

Status: accepted

## Context

Suppliers with a webhook URL are told when one of their orders is posted and when a delivery is
received. The first idea was a notifier that consumes `stockyard-events` next to the projections
worker. An SQS queue is point to point: with two consumers on one queue each message goes to one
of them, so each would see about half the events. Fanning out through SNS to a queue per consumer
would work, at the cost of another AWS resource, another set of permissions, and webhook state
(attempts, last status, next attempt) that still has to live somewhere queryable.

## Decision

`usp_PostPurchaseOrder` and `usp_ReceiveDelivery` insert a `WebhookDelivery` row in the same
transaction as the outbox row, when the supplier has a webhook URL. The notifier never reads the
queue. It:

1. claims due rows (`DeliveredAt` null, fewer than 8 attempts, `NextAttemptAt` passed) with
   `UPDLOCK, READPAST`, pushing `NextAttemptAt` forward by `Notifier__Lease` as the claim;
2. signs the body with `X-Stockyard-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256 of "<t>.<body>">`
   using `Notifier__SigningSecret`, and posts it with `X-Stockyard-Event` and
   `X-Stockyard-Delivery` headers;
3. records the outcome: delivered on any 2xx; otherwise `Attempts`, `LastStatusCode`,
   `LastError`, and the next attempt 1 s later, doubling to a 10 minute cap, for 8 attempts.

`POST /api/v1/webhooks/{deliveryId}/replay` puts a delivery back in the queue of due rows.

## Consequences

- Webhook state is a table: what was sent, when, with which status, is one query away.
- The queue has exactly one consumer, so the projections worker sees every event.
- The notifier's IAM role needs no SQS permissions at all, only the signing secret.
- The lease means a notifier that dies mid-send gives its rows back when the lease ends; the
  supplier may then see the same delivery twice and should deduplicate on
  `X-Stockyard-Delivery`, as webhook receivers generally must.
- The timestamp is inside the signed text, so a captured request cannot be replayed later; the
  fake supplier rejects signatures older than 5 minutes and verifies from the documented format
  without Stockyard's code.
