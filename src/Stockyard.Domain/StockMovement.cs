namespace Stockyard.Domain;

/// <summary>One row of the movement ledger, the source of truth for stock.</summary>
public sealed class StockMovement
{
    public long Id { get; set; }

    public Guid ItemId { get; set; }

    /// <summary>Null means the warehouse.</summary>
    public Guid? JobSiteId { get; set; }

    public MovementKind Kind { get; set; }

    /// <summary>Signed: receipts are positive, issues are negative.</summary>
    public decimal Quantity { get; set; }

    public required string Reference { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
