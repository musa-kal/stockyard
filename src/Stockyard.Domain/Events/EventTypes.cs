namespace Stockyard.Domain.Events;

/// <summary>Event type names, used in the outbox Type column and the SQS "type" attribute.</summary>
public static class EventTypes
{
    public const string PurchaseOrderPosted = nameof(Events.PurchaseOrderPosted);

    public const string DeliveryReceived = nameof(Events.DeliveryReceived);

    public const string StockIssued = nameof(Events.StockIssued);

    public const string StockReconciled = nameof(Events.StockReconciled);
}
