namespace Stockyard.Domain;

/// <summary>An event written in the same transaction as the change it describes.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public required string Type { get; set; }

    /// <summary>The event payload as camelCase JSON.</summary>
    public required string Payload { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
