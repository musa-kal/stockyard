namespace Stockyard.Domain;

/// <summary>Guard row: a consumer has applied a message. The key makes a second apply fail.</summary>
public sealed class ProcessedMessage
{
    public required string ConsumerName { get; set; }

    public Guid MessageId { get; set; }

    public DateTime ProcessedAt { get; set; }
}
