namespace Stockyard.Domain;

/// <summary>Lifecycle of a purchase order. Stored as tinyint, so the values are fixed.</summary>
public enum PurchaseOrderStatus : byte
{
    Draft = 0,
    Posted = 1,
    PartiallyReceived = 2,
    Received = 3,
    Cancelled = 4,
}
