namespace Stockyard.Messaging;

/// <summary>Bound from the "Messaging" section (Messaging__Provider, Messaging__QueueUrl, ...).</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public const string SqsProvider = "Sqs";

    public const string InMemoryProvider = "InMemory";

    /// <summary>"Sqs" or "InMemory".</summary>
    public string Provider { get; set; } = SqsProvider;

    public string QueueUrl { get; set; } = string.Empty;

    public string DlqUrl { get; set; } = string.Empty;

    /// <summary>Endpoint override for LocalStack. Empty on AWS, where the region decides.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public bool UsesSqs => string.Equals(Provider, SqsProvider, StringComparison.OrdinalIgnoreCase);
}
