/*
    dbo.usp_PostPurchaseOrder

    Purpose:     posts a draft purchase order. In one transaction it sets the order to Posted,
                 stamps PostedAt and the idempotency key, writes the PurchaseOrderPosted outbox row
                 and, when the supplier has a webhook URL, a WebhookDelivery row.
    Parameters:  @PurchaseOrderId  the order to post
                 @IdempotencyKey   client supplied key (1 to 64 characters). Posting again with the
                                   same key is a no-op, so a retried request returns the same order.
    Result sets: none. The caller reads the order afterwards.
    Errors:      50000  the key is missing or the order has no lines (the API enforces the
                        64 character limit, a longer value would be truncated by the parameter)
                 50001  the order was already posted under a different key
                 50004  the order does not exist
                 50005  the order is cancelled
                 A key already used by another order fails the unique index (2601).
    Locking:     UPDLOCK, HOLDLOCK on the order row for the whole transaction, so two posts of
                 the same order serialise and the second one sees the first one's key.
*/
CREATE OR ALTER PROCEDURE dbo.usp_PostPurchaseOrder
    @PurchaseOrderId uniqueidentifier,
    @IdempotencyKey nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @IdempotencyKey IS NULL OR LEN(@IdempotencyKey) = 0
        THROW 50000, N'An idempotency key is required to post a purchase order.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @Status tinyint,
            @ExistingKey nvarchar(64),
            @SupplierId uniqueidentifier,
            @Number nvarchar(20),
            @Message nvarchar(400);

        SELECT
            @Status = po.Status,
            @ExistingKey = po.IdempotencyKey,
            @SupplierId = po.SupplierId,
            @Number = po.Number
        FROM dbo.PurchaseOrder AS po WITH (UPDLOCK, HOLDLOCK)
        WHERE po.Id = @PurchaseOrderId;

        IF @Status IS NULL
            THROW 50004, N'Purchase order not found.', 1;

        IF @Status <> 0
        BEGIN
            IF @ExistingKey = @IdempotencyKey
            BEGIN
                -- A retry of the request that posted the order: nothing to do.
                COMMIT TRANSACTION;
                RETURN 0;
            END;

            IF @Status = 4
            BEGIN
                SET @Message = CONCAT(N'Purchase order ', @Number, N' is cancelled.');
                THROW 50005, @Message, 1;
            END;

            SET @Message = CONCAT(N'Purchase order ', @Number, N' was already posted with a different idempotency key.');
            THROW 50001, @Message, 1;
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrderLine AS l WHERE l.PurchaseOrderId = @PurchaseOrderId)
        BEGIN
            SET @Message = CONCAT(N'Purchase order ', @Number, N' has no lines.');
            THROW 50000, @Message, 1;
        END;

        DECLARE @Now datetime2 = SYSUTCDATETIME();

        UPDATE dbo.PurchaseOrder
        SET Status = 1,
            PostedAt = @Now,
            IdempotencyKey = @IdempotencyKey
        WHERE Id = @PurchaseOrderId;

        DECLARE @Payload nvarchar(max) =
        (
            SELECT
                po.Id AS orderId,
                po.Number AS number,
                s.Code AS supplierCode,
                js.Code AS jobSiteCode,
                (
                    SELECT
                        l.LineNumber AS lineNumber,
                        i.Sku AS itemSku,
                        l.QuantityOrdered AS quantityOrdered,
                        l.UnitCost AS unitCost
                    FROM dbo.PurchaseOrderLine AS l
                    INNER JOIN dbo.Item AS i
                        ON i.Id = l.ItemId
                    WHERE l.PurchaseOrderId = po.Id
                    ORDER BY l.LineNumber
                    FOR JSON PATH
                ) AS lines
            FROM dbo.PurchaseOrder AS po
            INNER JOIN dbo.Supplier AS s
                ON s.Id = po.SupplierId
            INNER JOIN dbo.JobSite AS js
                ON js.Id = po.JobSiteId
            WHERE po.Id = @PurchaseOrderId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        INSERT dbo.OutboxMessage (Type, Payload, OccurredAt, Attempts)
        VALUES (N'PurchaseOrderPosted', @Payload, @Now, 0);

        INSERT dbo.WebhookDelivery (SupplierId, EventType, Payload, Attempts, NextAttemptAt)
        SELECT s.Id, N'PurchaseOrderPosted', @Payload, 0, @Now
        FROM dbo.Supplier AS s
        WHERE s.Id = @SupplierId
            AND s.WebhookUrl IS NOT NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
