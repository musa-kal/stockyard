# 0002. Hand-written T-SQL procedures for posting, receiving and issuing

Status: accepted

## Context

The three operations that move money or stock have rules that depend on concurrent writers:

- an order must be posted once, even when the client retries;
- a delivery cannot take a line past its ordered quantity, even when two receipts arrive at once;
- an issue cannot take a site below zero, even when two crews draw the last units together.

Each also writes several tables and an outbox row. Written with EF Core, every operation is a
read, a check in C#, and a write, with the isolation and locking spread across the application
code and several round trips. EF Core still owns the schema and is the right tool for master
data and reads.

## Decision

`usp_PostPurchaseOrder`, `usp_ReceiveDelivery` (with the `ReceiptLineType` table type),
`usp_IssueStock` and `usp_ReconcileStock` are hand-written T-SQL, one object per file under
`src/Stockyard.Infrastructure/Sql/`, re-runnable with `CREATE OR ALTER`, embedded in the assembly
and installed by the `SqlObjects` migration. Each file starts with a header that states its
purpose, parameters, result sets, error numbers and locking.

Each procedure takes the lock it needs before it checks anything:

- post and receive take `UPDLOCK, HOLDLOCK` on the order row, so they serialise per order;
- issue takes an exclusive application lock (`sp_getapplock`) on the item and site pair, owned by
  the transaction, and then sums the ledger.

Business errors use `THROW` with numbers 50000 to 50005, the same numbers as
`Stockyard.Domain.ErrorNumbers`, so the api maps procedure errors and domain errors through one
table.

## Consequences

- The rule and its lock sit next to each other and run in one round trip. The concurrency test
  (`IssueStockTests.ConcurrentIssuesNeverOverdraw`) issues from twelve tasks and never overdraws.
- Receipts do not take the issue lock. They only add stock, which cannot make a checked issue
  overdraw, so receiving never waits on issuing.
- The procedures are code that EF Core migrations do not diff. A change to one needs a new
  migration that runs its script again; the scripts are re-runnable for that reason. The unit
  tests check that every embedded script is listed in the install order, has the header, and is
  re-runnable.
- Integration tests run against SQL Server 2022 in a container, because the procedures are the
  behaviour under test; an in-memory database would test nothing.
