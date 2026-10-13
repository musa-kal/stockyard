/*
    scripts/perf/measure.sql

    Purpose:     measures the stock on hand read for one item (dbo.vw_StockOnHand filtered on
                 ItemSku, the query behind GET /api/v1/stock/on-hand?itemSku=) in one of three
                 index states, with a warm cache.
    Parameters:  sqlcmd variables
                   Stage  none      no index leads with StockMovement.ItemId
                          fk        IX_StockMovement_ItemId, the single-column index EF Core
                                    creates for the foreign key
                          covering  IX_StockMovement_ItemId_OccurredAt INCLUDE (Quantity, JobSiteId)
                   Sku    the item to read, for example PERF-01000
    Result sets: the actual plan (STATISTICS PROFILE: rows and executions per operator), then
                 one row with the 200-query loop timing. STATISTICS IO and TIME print as messages.
    Errors:      none of its own.
    Locking:     index builds take a schema modification lock; run it on the perf database only.
                 The script leaves the database in the covering state the migrations create
                 only when Stage is covering.
*/
SET NOCOUNT ON;

IF '$(Stage)' NOT IN ('none', 'fk', 'covering')
    THROW 50000, N'Stage must be none, fk or covering.', 1;

IF '$(Stage)' <> 'covering' AND INDEXPROPERTY(OBJECT_ID(N'dbo.StockMovement'), N'IX_StockMovement_ItemId_OccurredAt', 'IndexID') IS NOT NULL
    DROP INDEX IX_StockMovement_ItemId_OccurredAt ON dbo.StockMovement;

IF '$(Stage)' <> 'fk' AND INDEXPROPERTY(OBJECT_ID(N'dbo.StockMovement'), N'IX_StockMovement_ItemId', 'IndexID') IS NOT NULL
    DROP INDEX IX_StockMovement_ItemId ON dbo.StockMovement;

IF '$(Stage)' = 'fk' AND INDEXPROPERTY(OBJECT_ID(N'dbo.StockMovement'), N'IX_StockMovement_ItemId', 'IndexID') IS NULL
    CREATE INDEX IX_StockMovement_ItemId ON dbo.StockMovement (ItemId);

IF '$(Stage)' = 'covering' AND INDEXPROPERTY(OBJECT_ID(N'dbo.StockMovement'), N'IX_StockMovement_ItemId_OccurredAt', 'IndexID') IS NULL
    CREATE INDEX IX_StockMovement_ItemId_OccurredAt ON dbo.StockMovement (ItemId, OccurredAt) INCLUDE (Quantity, JobSiteId);

UPDATE STATISTICS dbo.StockMovement WITH FULLSCAN;
DBCC FREEPROCCACHE WITH NO_INFOMSGS;

-- The api sends the SKU as a parameter, so the measurement does too.
DECLARE @query nvarchar(max) = N'
SELECT ItemSku, JobSiteCode, Quantity, LastMovementAt
FROM dbo.vw_StockOnHand
WHERE ItemSku = @sku
ORDER BY ItemSku, JobSiteCode;';
DECLARE @loopQuery nvarchar(max) = N'
DECLARE @quantity decimal(18, 3);
SELECT @quantity = Quantity
FROM dbo.vw_StockOnHand
WHERE ItemSku = @sku;';
DECLARE @sku nvarchar(32) = N'$(Sku)';

-- Warm the buffer pool and the plan cache.
CREATE TABLE #warm (ItemSku nvarchar(32), JobSiteCode nvarchar(16), Quantity decimal(18, 3), LastMovementAt datetime2);
INSERT #warm EXEC sys.sp_executesql @query, N'@sku nvarchar(32)', @sku = @sku;
DROP TABLE #warm;

PRINT '--- single query: STATISTICS IO and TIME, actual plan ---';
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
SET STATISTICS PROFILE ON;
EXEC sys.sp_executesql @query, N'@sku nvarchar(32)', @sku = @sku;
SET STATISTICS PROFILE OFF;
SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;

PRINT '--- 200-query loop ---';
DECLARE @i int = 0;
DECLARE @started datetime2(7) = SYSDATETIME();
WHILE @i < 200
BEGIN
    EXEC sys.sp_executesql @loopQuery, N'@sku nvarchar(32)', @sku = @sku;
    SET @i += 1;
END;
DECLARE @elapsedMicroseconds bigint = DATEDIFF_BIG(MICROSECOND, @started, SYSDATETIME());

SELECT
    '$(Stage)' AS Stage,
    200 AS Queries,
    CAST(@elapsedMicroseconds / 1000.0 AS decimal(12, 1)) AS TotalMs,
    CAST(@elapsedMicroseconds / 200.0 / 1000.0 AS decimal(12, 3)) AS AverageMs;
