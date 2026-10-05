/*
    scripts/perf/seed-movements.sql

    Purpose:     the data set for the read-path measurement in docs/performance.md:
                 2,000 items, 20 job sites and 1,000,000 stock movements, inserted set based.
                 Each item gets 500 movements spread through the whole table (row r belongs to
                 item r % 2000), the way a real ledger interleaves items over time. An item's
                 movements rotate over the warehouse (null JobSiteId) and the 20 sites, and every
                 fourth one is an issue, so every item has a positive balance at 21 places.
    Parameters:  none. Run against a migrated database other than the everyday one.
    Result sets: none; prints the row counts.
    Errors:      none of its own. Does nothing when PERF-00001 already exists.
    Locking:     TABLOCK on the movement insert for minimal logging; nothing else should be
                 running against this database.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.Item WHERE Sku = N'PERF-00001')
BEGIN
    DECLARE @existing bigint = (SELECT COUNT_BIG(*) FROM dbo.StockMovement);
    PRINT CONCAT('perf data already present: ', @existing, ' movements');
    RETURN;
END;

WITH numbers AS
(
    SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
)
INSERT dbo.Item (Sku, Description, UnitOfMeasure, UnitCost, ReorderPoint)
SELECT
    CONCAT(N'PERF-', RIGHT(CONCAT(N'0000', n), 5)),
    CONCAT(N'Performance test item ', n),
    N'EA',
    1 + n % 50,
    10
FROM numbers;

WITH numbers AS
(
    SELECT TOP (20) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_objects
)
INSERT dbo.JobSite (Code, Name, Active)
SELECT CONCAT(N'PERF-S', RIGHT(CONCAT(N'0', n), 2)), CONCAT(N'Performance test site ', n), 1
FROM numbers;

DECLARE @now datetime2 = SYSUTCDATETIME();

WITH numbers AS
(
    SELECT TOP (1000000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS r
    FROM sys.all_objects AS a
    CROSS JOIN sys.all_objects AS b
    CROSS JOIN sys.all_objects AS c
),
items AS
(
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Sku) - 1 AS k
    FROM dbo.Item
    WHERE Sku LIKE N'PERF-%'
),
sites AS
(
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Code) - 1 AS k
    FROM dbo.JobSite
    WHERE Code LIKE N'PERF-S%'
)
INSERT dbo.StockMovement WITH (TABLOCK) (ItemId, JobSiteId, Kind, Quantity, Reference, OccurredAt)
SELECT
    i.Id,
    s.Id,                                             -- null for the warehouse
    CASE WHEN n.r / 2000 % 4 = 3 THEN 2 ELSE 1 END,   -- every fourth movement of an item is an issue
    CASE WHEN n.r / 2000 % 4 = 3 THEN -1.000 ELSE 2.000 END,
    CONCAT(N'PERF-', n.r),
    DATEADD(SECOND, -(n.r % 31536000), @now)          -- spread over the last year
FROM numbers AS n
INNER JOIN items AS i
    ON i.k = n.r % 2000
LEFT JOIN sites AS s
    ON s.k + 1 = n.r / 2000 % 21;                     -- place 0 is the warehouse

DECLARE @items int = (SELECT COUNT(*) FROM dbo.Item WHERE Sku LIKE N'PERF-%');
DECLARE @movements bigint = (SELECT COUNT_BIG(*) FROM dbo.StockMovement);
PRINT CONCAT('seeded ', @items, ' items and ', @movements, ' movements');
