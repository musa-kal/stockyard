/*
    dbo.vw_StockOnHand

    Purpose:     stock on hand computed from the movement ledger, one row per item and site.
                 The ledger stores the warehouse as a null JobSiteId; the view reports it as the
                 WAREHOUSE job site (the row with the empty GUID).
    Parameters:  none. Filter on ItemSku, ItemId or JobSiteCode.
    Result set:  ItemId, ItemSku, JobSiteId, JobSiteCode, Quantity, LastMovementAt
    Errors:      none.
    Locking:     reader only. Filtering on one item should seek IX_StockMovement_ItemId_OccurredAt,
                 which covers ItemId, OccurredAt, Quantity and JobSiteId (see docs/performance.md).
*/
CREATE OR ALTER VIEW dbo.vw_StockOnHand
AS
SELECT
    s.ItemId,
    i.Sku AS ItemSku,
    s.JobSiteId,
    js.Code AS JobSiteCode,
    s.Quantity,
    s.LastMovementAt
FROM
(
    SELECT
        m.ItemId,
        ISNULL(m.JobSiteId, CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')) AS JobSiteId,
        SUM(m.Quantity) AS Quantity,
        MAX(m.OccurredAt) AS LastMovementAt
    FROM dbo.StockMovement AS m
    GROUP BY
        m.ItemId,
        ISNULL(m.JobSiteId, CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000'))
) AS s
INNER JOIN dbo.Item AS i
    ON i.Id = s.ItemId
INNER JOIN dbo.JobSite AS js
    ON js.Id = s.JobSiteId;
