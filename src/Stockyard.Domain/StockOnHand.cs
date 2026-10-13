namespace Stockyard.Domain;

/// <summary>
/// Projection of the ledger per item and site, maintained by the projections worker.
/// The warehouse is stored as <see cref="JobSite.WarehouseId"/> because the key cannot be null.
/// </summary>
public sealed class StockOnHand
{
    public Guid ItemId { get; set; }

    public Guid JobSiteId { get; set; }

    public decimal Quantity { get; set; }

    public DateTime UpdatedAt { get; set; }
}
