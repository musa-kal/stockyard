namespace Stockyard.Messaging.Outbox;

/// <summary>Bound from the "Outbox" section (Outbox__PollInterval, Outbox__BatchSize).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How long the relay sleeps when the last batch was not full.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;
}
