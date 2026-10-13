namespace Stockyard.Domain;

/// <summary>Validation for free-text fields such as names and descriptions.</summary>
public static class Text
{
    public static string Required(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{field} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
