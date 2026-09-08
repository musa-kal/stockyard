namespace Stockyard.Infrastructure.Queries;

/// <summary>A row of dbo.vw_StockOnHand.</summary>
public sealed class StockOnHandRow
{
    public required string ItemSku { get; init; }

    public required string JobSiteCode { get; init; }

    public decimal Quantity { get; init; }

    public DateTime LastMovementAt { get; init; }
}
