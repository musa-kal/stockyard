namespace Stockyard.Domain;

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }

    public required string Number { get; set; }

    public Guid SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public Guid JobSiteId { get; set; }

    public JobSite? JobSite { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public string? IdempotencyKey { get; set; }

    public DateTime? PostedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public List<PurchaseOrderLine> Lines { get; } = [];

    /// <summary>
    /// Adds a line to a draft order. The unit cost defaults to the item's current cost.
    /// </summary>
    public PurchaseOrderLine AddLine(Item item, decimal quantityOrdered, decimal? unitCost = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new DomainException(ErrorNumbers.InvalidState, $"Purchase order {Number} is {Status}; lines can only be added to a draft.");
        }

        if (quantityOrdered <= 0)
        {
            throw new DomainException("quantityOrdered must be greater than zero.");
        }

        var cost = unitCost ?? item.UnitCost;
        if (cost < 0)
        {
            throw new DomainException("unitCost cannot be negative.");
        }

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = Id,
            LineNumber = Lines.Count == 0 ? 1 : Lines.Max(l => l.LineNumber) + 1,
            ItemId = item.Id,
            Item = item,
            QuantityOrdered = quantityOrdered,
            UnitCost = cost,
        };
        Lines.Add(line);
        return line;
    }
}
