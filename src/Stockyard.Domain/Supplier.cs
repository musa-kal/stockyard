namespace Stockyard.Domain;

public sealed class Supplier
{
    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? WebhookUrl { get; set; }

    public DateTime CreatedAt { get; set; }
}
