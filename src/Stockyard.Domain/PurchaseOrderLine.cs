namespace Stockyard.Domain;

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; }

    public Guid PurchaseOrderId { get; set; }

    public int LineNumber { get; set; }

    public Guid ItemId { get; set; }

    public Item? Item { get; set; }

    public decimal QuantityOrdered { get; set; }

    public decimal QuantityReceived { get; set; }

    public decimal UnitCost { get; set; }

    /// <summary>Persisted computed column: QuantityOrdered * UnitCost.</summary>
    public decimal LineTotal { get; private set; }
}
