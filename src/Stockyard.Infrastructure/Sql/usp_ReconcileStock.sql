/*
    dbo.usp_ReconcileStock

    Purpose:     compares the StockOnHand projection with the movement ledger and, when @Apply is
                 1, corrects the projection in the same transaction (ADR 0005). When rows were
                 corrected it also writes a StockReconciled outbox row, which is informational.
                 Item and site pairs with an event the projections consumer has not applied yet
                 are skipped: the projection is behind there, not wrong, and correcting it now
                 would count the event twice once it arrives. Events older than 14 days (the
                 longest an SQS message can be retained) are no longer treated as in flight.
    Parameters:  @Apply  1 (default) corrects the projection; 0 only reports, holds no locks
                         beyond read committed and writes nothing.
    Result set:  ItemSku, JobSiteCode, Projected, Actual, one row per pair that differed.
    Errors:      none of its own.
    Locking:     with @Apply = 1 the transaction runs SERIALIZABLE and reads the ledger first,
                 then the outbox and guard rows, then the projection. Writers take their locks in
                 the same order (movement, then outbox; guard, then projection), so new movements
                 and projection updates wait for the reconcile instead of deadlocking with it.
*/
CREATE OR ALTER PROCEDURE dbo.usp_ReconcileStock
    @Apply bit = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Apply = 1
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    DECLARE @Warehouse uniqueidentifier = CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000');

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Ledger TABLE
        (
            ItemId uniqueidentifier NOT NULL,
            JobSiteId uniqueidentifier NOT NULL,
            Quantity decimal(18, 3) NOT NULL,
            PRIMARY KEY (ItemId, JobSiteId)
        );

        INSERT @Ledger (ItemId, JobSiteId, Quantity)
        SELECT m.ItemId, ISNULL(m.JobSiteId, @Warehouse), SUM(m.Quantity)
        FROM dbo.StockMovement AS m
        GROUP BY m.ItemId, ISNULL(m.JobSiteId, @Warehouse);

        DECLARE @InFlight TABLE
        (
            ItemSku nvarchar(32) NOT NULL,
            JobSiteCode nvarchar(16) NOT NULL,
            PRIMARY KEY (ItemSku, JobSiteCode)
        );

        INSERT @InFlight (ItemSku, JobSiteCode)
        SELECT DISTINCT k.ItemSku, k.JobSiteCode
        FROM dbo.OutboxMessage AS o
        CROSS APPLY
        (
            SELECT
                CAST(JSON_VALUE(o.Payload, '$.itemSku') AS nvarchar(32)) AS ItemSku,
                CAST(JSON_VALUE(o.Payload, '$.jobSiteCode') AS nvarchar(16)) AS JobSiteCode
            WHERE o.Type = N'StockIssued'
            UNION ALL
            SELECT
                l.itemSku,
                CAST(JSON_VALUE(o.Payload, '$.jobSiteCode') AS nvarchar(16))
            FROM OPENJSON(o.Payload, '$.lines') WITH (itemSku nvarchar(32) '$.itemSku') AS l
            WHERE o.Type = N'DeliveryReceived'
        ) AS k
        WHERE o.Type IN (N'StockIssued', N'DeliveryReceived')
            AND o.OccurredAt >= DATEADD(DAY, -14, SYSUTCDATETIME())
            AND NOT EXISTS
            (
                -- 'projections' is the consumer name the projections worker records.
                SELECT 1
                FROM dbo.ProcessedMessage AS p
                WHERE p.ConsumerName = N'projections'
                    AND p.MessageId = o.Id
            );

        DECLARE @Differences TABLE
        (
            ItemId uniqueidentifier NOT NULL,
            JobSiteId uniqueidentifier NOT NULL,
            Projected decimal(18, 3) NOT NULL,
            Actual decimal(18, 3) NOT NULL,
            HasProjection bit NOT NULL,
            PRIMARY KEY (ItemId, JobSiteId)
        );

        INSERT @Differences (ItemId, JobSiteId, Projected, Actual, HasProjection)
        SELECT
            COALESCE(l.ItemId, p.ItemId),
            COALESCE(l.JobSiteId, p.JobSiteId),
            ISNULL(p.Quantity, 0),
            ISNULL(l.Quantity, 0),
            CASE WHEN p.ItemId IS NULL THEN 0 ELSE 1 END
        FROM @Ledger AS l
        FULL OUTER JOIN dbo.StockOnHand AS p WITH (UPDLOCK)
            ON p.ItemId = l.ItemId
            AND p.JobSiteId = l.JobSiteId
        WHERE ISNULL(p.Quantity, 0) <> ISNULL(l.Quantity, 0);

        DELETE d
        FROM @Differences AS d
        INNER JOIN dbo.Item AS i
            ON i.Id = d.ItemId
        INNER JOIN dbo.JobSite AS js
            ON js.Id = d.JobSiteId
        INNER JOIN @InFlight AS f
            ON f.ItemSku = i.Sku
            AND f.JobSiteCode = js.Code;

        IF @Apply = 1 AND EXISTS (SELECT 1 FROM @Differences)
        BEGIN
            DECLARE @Now datetime2 = SYSUTCDATETIME();

            UPDATE p
            SET Quantity = d.Actual,
                UpdatedAt = @Now
            FROM dbo.StockOnHand AS p
            INNER JOIN @Differences AS d
                ON d.ItemId = p.ItemId
                AND d.JobSiteId = p.JobSiteId;

            INSERT dbo.StockOnHand (ItemId, JobSiteId, Quantity, UpdatedAt)
            SELECT d.ItemId, d.JobSiteId, d.Actual, @Now
            FROM @Differences AS d
            WHERE d.HasProjection = 0;

            DECLARE @Payload nvarchar(max) =
            (
                SELECT
                (
                    SELECT
                        i.Sku AS itemSku,
                        js.Code AS jobSiteCode,
                        d.Projected AS projected,
                        d.Actual AS actual
                    FROM @Differences AS d
                    INNER JOIN dbo.Item AS i
                        ON i.Id = d.ItemId
                    INNER JOIN dbo.JobSite AS js
                        ON js.Id = d.JobSiteId
                    ORDER BY i.Sku, js.Code
                    FOR JSON PATH
                ) AS differences
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
            );

            INSERT dbo.OutboxMessage (Type, Payload, OccurredAt, Attempts)
            VALUES (N'StockReconciled', @Payload, @Now, 0);
        END;

        COMMIT TRANSACTION;

        SELECT
            i.Sku AS ItemSku,
            js.Code AS JobSiteCode,
            d.Projected,
            d.Actual
        FROM @Differences AS d
        INNER JOIN dbo.Item AS i
            ON i.Id = d.ItemId
        INNER JOIN dbo.JobSite AS js
            ON js.Id = d.JobSiteId
        ORDER BY i.Sku, js.Code;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
