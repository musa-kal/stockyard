namespace Stockyard.Domain;

/// <summary>A webhook owed to a supplier, drained and retried by the notifier.</summary>
public sealed class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }

    public required string EventType { get; set; }

    public required string Payload { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }
}
