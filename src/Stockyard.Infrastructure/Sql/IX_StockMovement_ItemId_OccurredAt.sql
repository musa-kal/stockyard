/*
    IX_StockMovement_ItemId_OccurredAt

    Purpose:     covering index for the per-item reads of the ledger: vw_StockOnHand filtered on
                 one item, the balance check in usp_IssueStock, and the exporter's movement scan.
                 Keyed on (ItemId, OccurredAt) and including Quantity and JobSiteId, so those
                 reads seek and never look up the clustered index.
    Parameters:  none.
    Result sets: none.
    Errors:      none.
    Locking:     an offline index build takes a schema modification lock on dbo.StockMovement
                 for its duration; it runs inside the SqlObjects migration.

    EF Core creates IX_StockMovement_ItemId for the foreign key. This index leads with the same
    column, so it serves the foreign key as well, and the single-column index is dropped rather
    than maintained twice on every insert (docs/performance.md measures all three states).
*/
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.StockMovement')
        AND name = N'IX_StockMovement_ItemId_OccurredAt'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_StockMovement_ItemId_OccurredAt
        ON dbo.StockMovement (ItemId, OccurredAt)
        INCLUDE (Quantity, JobSiteId);
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.StockMovement')
        AND name = N'IX_StockMovement_ItemId'
)
BEGIN
    DROP INDEX IX_StockMovement_ItemId ON dbo.StockMovement;
END;
