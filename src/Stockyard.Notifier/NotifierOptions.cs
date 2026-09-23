namespace Stockyard.Notifier;

/// <summary>Bound from the "Notifier" section (Notifier__SigningSecret, Notifier__PollInterval, ...).</summary>
public sealed class NotifierOptions
{
    public const string SectionName = "Notifier";

    /// <summary>Shared with each supplier to verify X-Stockyard-Signature. Required.</summary>
    public string SigningSecret { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 20;

    /// <summary>How long a claimed delivery stays hidden from other notifier replicas.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
