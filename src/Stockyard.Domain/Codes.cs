namespace Stockyard.Domain;

/// <summary>
/// Normalises the business keys (supplier, job site and item codes) the API accepts.
/// Keys are trimmed and upper-cased on the way in, so lookups never depend on casing.
/// </summary>
public static class Codes
{
    public const int CodeMaxLength = 16;

    public const int SkuMaxLength = 32;

    public static string NormalizeCode(string? value, string field) => Normalize(value, CodeMaxLength, field);

    public static string NormalizeSku(string? value, string field) => Normalize(value, SkuMaxLength, field);

    public static string Normalize(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{field} is required.");
        }

        var upper = trimmed.ToUpperInvariant();
        if (upper.Length > maxLength)
        {
            throw new DomainException($"{field} must be at most {maxLength} characters.");
        }

        foreach (var c in upper)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_' or '.'))
            {
                throw new DomainException($"{field} may only contain letters, digits, '-', '_' and '.'.");
            }
        }

        return upper;
    }
}
