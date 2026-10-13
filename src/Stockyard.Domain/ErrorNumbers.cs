namespace Stockyard.Domain;

/// <summary>
/// Error numbers shared by the domain rules and the T-SQL procedures (THROW uses the same values),
/// so the API maps both through one table.
/// </summary>
public static class ErrorNumbers
{
    /// <summary>The request breaks a validation rule.</summary>
    public const int Validation = 50000;

    /// <summary>The purchase order was already posted under a different idempotency key.</summary>
    public const int IdempotencyKeyConflict = 50001;

    /// <summary>A receipt would take a line past its ordered quantity.</summary>
    public const int OverReceipt = 50002;

    /// <summary>An issue would take stock on hand below zero.</summary>
    public const int InsufficientStock = 50003;

    /// <summary>The referenced row does not exist.</summary>
    public const int NotFound = 50004;

    /// <summary>The purchase order is not in a status that allows the operation.</summary>
    public const int InvalidState = 50005;
}
