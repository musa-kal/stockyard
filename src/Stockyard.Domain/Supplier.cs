namespace Stockyard.Domain;

public sealed class Supplier
{
    public const int NameMaxLength = 120;

    public const int WebhookUrlMaxLength = 400;

    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? WebhookUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public static Supplier Create(string? code, string? name, string? webhookUrl) => new()
    {
        Code = Codes.NormalizeCode(code, "code"),
        Name = Text.Required(name, NameMaxLength, "name"),
        WebhookUrl = NormalizeWebhookUrl(webhookUrl),
    };

    /// <summary>An optional absolute http or https URL; blank means no webhook.</summary>
    public static string? NormalizeWebhookUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > WebhookUrlMaxLength)
        {
            throw new DomainException($"webhookUrl must be at most {WebhookUrlMaxLength} characters.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("webhookUrl must be an absolute http or https URL.");
        }

        return uri.AbsoluteUri;
    }
}
