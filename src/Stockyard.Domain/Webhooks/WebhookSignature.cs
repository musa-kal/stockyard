using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Stockyard.Domain.Webhooks;

/// <summary>
/// Signs webhook bodies as <c>X-Stockyard-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "&lt;t&gt;.&lt;body&gt;"&gt;</c>.
/// The timestamp is part of the signed text so a captured request cannot be replayed later.
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-Stockyard-Signature";

    public static string CreateHeader(string secret, long timestamp, string body)
    {
        return $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={ComputeHex(secret, timestamp, body)}";
    }

    public static string ComputeHex(string secret, long timestamp, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(body);

        var signed = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
        return Convert.ToHexStringLower(mac);
    }

    public static bool TryParse(string? header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                if (!long.TryParse(part.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out timestamp))
                {
                    return false;
                }
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signature = part[3..];
            }
        }

        return timestamp > 0 && signature.Length > 0;
    }

    /// <summary>Checks the signature and that the timestamp is within the tolerance of now.</summary>
    public static bool Verify(string secret, string? header, string body, DateTimeOffset now, TimeSpan tolerance)
    {
        if (!TryParse(header, out var timestamp, out var signature))
        {
            return false;
        }

        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(ComputeHex(secret, timestamp, body));
        var actual = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
