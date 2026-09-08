namespace Stockyard.Infrastructure.Queries;

/// <summary>A row of dbo.vw_OpenPurchaseOrders.</summary>
public sealed class OpenPurchaseOrderLineRow
{
    public Guid PurchaseOrderId { get; init; }

    public required string Number { get; init; }

    public byte Status { get; init; }

    public required string SupplierCode { get; init; }

    public required string JobSiteCode { get; init; }

    public DateTime? PostedAt { get; init; }

    public int LineNumber { get; init; }

    public required string ItemSku { get; init; }

    public decimal QuantityOrdered { get; init; }

    public decimal QuantityReceived { get; init; }

    public decimal QuantityOutstanding { get; init; }

    public decimal UnitCost { get; init; }
}
