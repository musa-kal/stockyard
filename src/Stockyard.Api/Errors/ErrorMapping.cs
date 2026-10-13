using Stockyard.Domain;

namespace Stockyard.Api.Errors;

/// <summary>
/// The single table from error numbers (domain rules, procedure THROWs and SQL Server key
/// violations) to HTTP status codes and Problem Details titles.
/// </summary>
public static class ErrorMapping
{
    public const int UniqueIndexViolation = 2601;
    public const int PrimaryKeyViolation = 2627;

    /// <summary>usp_IssueStock could not take its per item and site lock in time.</summary>
    public const int StockLockTimeout = 51000;

    public static (int Status, string Title)? Map(int errorNumber) => errorNumber switch
    {
        ErrorNumbers.Validation => (StatusCodes.Status400BadRequest, "The request is not valid."),
        ErrorNumbers.OverReceipt => (StatusCodes.Status400BadRequest, "The delivery exceeds the quantity ordered."),
        ErrorNumbers.IdempotencyKeyConflict => (StatusCodes.Status409Conflict, "The order was posted with a different idempotency key."),
        ErrorNumbers.InsufficientStock => (StatusCodes.Status409Conflict, "Not enough stock on hand."),
        ErrorNumbers.InvalidState => (StatusCodes.Status409Conflict, "The purchase order is not in a status that allows this."),
        ErrorNumbers.NotFound => (StatusCodes.Status404NotFound, "Not found."),
        UniqueIndexViolation or PrimaryKeyViolation => (StatusCodes.Status409Conflict, "A record with the same key already exists."),
        StockLockTimeout => (StatusCodes.Status503ServiceUnavailable, "Stock is busy; retry the request."),
        _ => null,
    };
}
