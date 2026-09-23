namespace Stockyard.Notifier;

/// <summary>A delivery leased by this notifier, with the supplier's current webhook URL.</summary>
public sealed record ClaimedDelivery(Guid Id, Guid SupplierId, string EventType, string Payload, int Attempts, string? WebhookUrl);
