/*
    dbo.usp_ReceiveDelivery

    Purpose:     records a delivery against a posted purchase order. In one transaction it adds
                 the received quantities to the lines, writes one Receipt movement per line at the
                 order's job site (null for the warehouse), moves the order to PartiallyReceived or
                 Received, and writes the DeliveryReceived outbox row and, when the supplier has a
                 webhook URL, a WebhookDelivery row.
    Parameters:  @PurchaseOrderId  the order being received
                 @Lines            dbo.ReceiptLineType; repeated line numbers are summed
    Result sets: none. The caller reads the order afterwards.
    Errors:      50000  no lines, a quantity that is not positive, or an unknown line number
                 50002  a line would be received past its ordered quantity
                 50004  the order does not exist
                 50005  the order is a draft or cancelled
    Locking:     UPDLOCK, HOLDLOCK on the order row, so concurrent receipts of the same order
                 serialise and the over-receipt check sees the previous delivery.
*/
CREATE OR ALTER PROCEDURE dbo.usp_ReceiveDelivery
    @PurchaseOrderId uniqueidentifier,
    @Lines dbo.ReceiptLineType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @Status tinyint,
            @SupplierId uniqueidentifier,
            @JobSiteId uniqueidentifier,
            @Number nvarchar(20),
            @Message nvarchar(400);

        SELECT
            @Status = po.Status,
            @SupplierId = po.SupplierId,
            @JobSiteId = po.JobSiteId,
            @Number = po.Number
        FROM dbo.PurchaseOrder AS po WITH (UPDLOCK, HOLDLOCK)
        WHERE po.Id = @PurchaseOrderId;

        IF @Status IS NULL
            THROW 50004, N'Purchase order not found.', 1;

        IF @Status IN (0, 4)
        BEGIN
            SET @Message = CONCAT(N'Purchase order ', @Number, N' is ', CASE @Status WHEN 0 THEN N'a draft' ELSE N'cancelled' END, N' and cannot be received.');
            THROW 50005, @Message, 1;
        END;

        DECLARE @Receipt TABLE
        (
            LineNumber int NOT NULL PRIMARY KEY,
            Quantity decimal(18, 3) NOT NULL
        );

        INSERT @Receipt (LineNumber, Quantity)
        SELECT r.LineNumber, SUM(r.Quantity)
        FROM @Lines AS r
        GROUP BY r.LineNumber;

        IF NOT EXISTS (SELECT 1 FROM @Receipt)
            THROW 50000, N'A delivery needs at least one line.', 1;

        IF EXISTS (SELECT 1 FROM @Receipt WHERE Quantity <= 0)
            THROW 50000, N'Received quantities must be greater than zero.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM @Receipt AS r
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.PurchaseOrderLine AS l
                WHERE l.PurchaseOrderId = @PurchaseOrderId
                    AND l.LineNumber = r.LineNumber
            )
        )
        BEGIN
            SET @Message = CONCAT(N'Purchase order ', @Number, N' has no line with that number.');
            THROW 50000, @Message, 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM @Receipt AS r
            INNER JOIN dbo.PurchaseOrderLine AS l
                ON l.PurchaseOrderId = @PurchaseOrderId
                AND l.LineNumber = r.LineNumber
            WHERE l.QuantityReceived + r.Quantity > l.QuantityOrdered
        )
        BEGIN
            SET @Message = CONCAT(N'The delivery would receive more than was ordered on purchase order ', @Number, N'.');
            THROW 50002, @Message, 1;
        END;

        DECLARE @Now datetime2 = SYSUTCDATETIME();

        UPDATE l
        SET QuantityReceived = l.QuantityReceived + r.Quantity
        FROM dbo.PurchaseOrderLine AS l
        INNER JOIN @Receipt AS r
            ON r.LineNumber = l.LineNumber
        WHERE l.PurchaseOrderId = @PurchaseOrderId;

        INSERT dbo.StockMovement (ItemId, JobSiteId, Kind, Quantity, Reference, OccurredAt)
        SELECT
            l.ItemId,
            NULLIF(@JobSiteId, CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')),
            1, -- Receipt
            r.Quantity,
            @Number,
            @Now
        FROM @Receipt AS r
        INNER JOIN dbo.PurchaseOrderLine AS l
            ON l.PurchaseOrderId = @PurchaseOrderId
            AND l.LineNumber = r.LineNumber;

        UPDATE dbo.PurchaseOrder
        SET Status = CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.PurchaseOrderLine AS l
                    WHERE l.PurchaseOrderId = @PurchaseOrderId
                        AND l.QuantityReceived < l.QuantityOrdered
                ) THEN 2 -- PartiallyReceived
                ELSE 3   -- Received
            END
        WHERE Id = @PurchaseOrderId;

        DECLARE @Payload nvarchar(max) =
        (
            SELECT
                @PurchaseOrderId AS orderId,
                js.Code AS jobSiteCode,
                (
                    SELECT
                        r.LineNumber AS lineNumber,
                        i.Sku AS itemSku,
                        r.Quantity AS quantity
                    FROM @Receipt AS r
                    INNER JOIN dbo.PurchaseOrderLine AS l
                        ON l.PurchaseOrderId = @PurchaseOrderId
                        AND l.LineNumber = r.LineNumber
                    INNER JOIN dbo.Item AS i
                        ON i.Id = l.ItemId
                    ORDER BY r.LineNumber
                    FOR JSON PATH
                ) AS lines
            FROM dbo.JobSite AS js
            WHERE js.Id = @JobSiteId
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        );

        INSERT dbo.OutboxMessage (Type, Payload, OccurredAt, Attempts)
        VALUES (N'DeliveryReceived', @Payload, @Now, 0);

        INSERT dbo.WebhookDelivery (SupplierId, EventType, Payload, Attempts, NextAttemptAt)
        SELECT s.Id, N'DeliveryReceived', @Payload, 0, @Now
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
