/*
    dbo.usp_IssueStock

    Purpose:     issues stock from a site to the work. In one transaction it checks the balance in
                 the ledger, writes a negative Issue movement and the StockIssued outbox row.
    Parameters:  @ItemId     the item issued
                 @JobSiteId  the site the stock leaves; the empty GUID means the warehouse
                 @Quantity   positive quantity to issue
                 @Reference  free text such as a work order or a crew name (1 to 40 characters)
    Result set:  one row, MovementId and OccurredAt of the Issue movement.
    Errors:      50000  the quantity is not positive or the reference is missing
                 50003  the issue would take stock on hand below zero
                 50004  the item or the job site does not exist
                 51000  the stock lock could not be taken within 10 seconds (the api answers 503)
    Locking:     an exclusive application lock per (item, site), owned by the transaction. Two
                 issues of the same item at the same site run one after the other, so the balance
                 one of them reads cannot be spent by the other. Receipts do not take the lock:
                 they only add stock, which cannot make a checked issue overdraw.
*/
CREATE OR ALTER PROCEDURE dbo.usp_IssueStock
    @ItemId uniqueidentifier,
    @JobSiteId uniqueidentifier,
    @Quantity decimal(18, 3),
    @Reference nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Quantity IS NULL OR @Quantity <= 0
        THROW 50000, N'The quantity to issue must be greater than zero.', 1;

    IF @Reference IS NULL OR LEN(@Reference) = 0
        THROW 50000, N'A reference is required to issue stock.', 1;

    DECLARE
        @Warehouse uniqueidentifier = CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000'),
        @ItemSku nvarchar(32),
        @JobSiteCode nvarchar(16),
        @Message nvarchar(400);

    SELECT @ItemSku = i.Sku FROM dbo.Item AS i WHERE i.Id = @ItemId;
    IF @ItemSku IS NULL
        THROW 50004, N'Item not found.', 1;

    SELECT @JobSiteCode = js.Code FROM dbo.JobSite AS js WHERE js.Id = @JobSiteId;
    IF @JobSiteCode IS NULL
        THROW 50004, N'Job site not found.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @Resource nvarchar(255) = CONCAT(N'stockyard:issue:', @ItemId, N':', @JobSiteId),
            @LockResult int;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 10000;

        IF @LockResult < 0
            THROW 51000, N'Timed out waiting for the stock lock.', 1;

        DECLARE @OnHand decimal(18, 3) =
        (
            SELECT ISNULL(SUM(m.Quantity), 0)
            FROM dbo.StockMovement AS m
            WHERE m.ItemId = @ItemId
                AND ISNULL(m.JobSiteId, @Warehouse) = @JobSiteId
        );

        IF @OnHand < @Quantity
        BEGIN
            SET @Message = CONCAT(N'Cannot issue ', @Quantity, N' of ', @ItemSku, N' at ', @JobSiteCode, N': ', @OnHand, N' on hand.');
            THROW 50003, @Message, 1;
        END;

        DECLARE @Now datetime2 = SYSUTCDATETIME();

        INSERT dbo.StockMovement (ItemId, JobSiteId, Kind, Quantity, Reference, OccurredAt)
        VALUES (@ItemId, NULLIF(@JobSiteId, @Warehouse), 2, -@Quantity, @Reference, @Now); -- 2 = Issue

        DECLARE @MovementId bigint = SCOPE_IDENTITY();

        DECLARE @Payload nvarchar(max) =
        (
            SELECT
                @ItemSku AS itemSku,
                @JobSiteCode AS jobSiteCode,
                @Quantity AS quantity,
                @Reference AS reference
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        INSERT dbo.OutboxMessage (Type, Payload, OccurredAt, Attempts)
        VALUES (N'StockIssued', @Payload, @Now, 0);

        COMMIT TRANSACTION;

        SELECT @MovementId AS MovementId, @Now AS OccurredAt;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
