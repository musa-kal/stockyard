/*
    dbo.ReceiptLineType

    Purpose:     the lines of one delivery, passed to dbo.usp_ReceiveDelivery as a table-valued
                 parameter so a receipt of many lines is one round trip and one transaction.
    Columns:     LineNumber  purchase order line number
                 Quantity    quantity received on this delivery, must be positive
    Result sets: none.
    Errors:      none. Validation happens in the procedure, which also sums repeated line numbers.
    Locking:     none.

    A table type cannot be altered while a procedure references it, so this script only creates
    it when it is missing. Changing the shape needs a migration that drops the procedure first.
*/
IF TYPE_ID(N'dbo.ReceiptLineType') IS NULL
BEGIN
    CREATE TYPE dbo.ReceiptLineType AS TABLE
    (
        LineNumber int NOT NULL,
        Quantity decimal(18, 3) NOT NULL
    );
END;
