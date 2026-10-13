namespace Stockyard.Domain.Events;

public sealed record PurchaseOrderPosted(
    Guid OrderId,
    string Number,
    string SupplierCode,
    string JobSiteCode,
    IReadOnlyList<PurchaseOrderPostedLine> Lines);

public sealed record PurchaseOrderPostedLine(int LineNumber, string ItemSku, decimal QuantityOrdered, decimal UnitCost);
