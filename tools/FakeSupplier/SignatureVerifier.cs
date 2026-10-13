using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FakeSupplier;

/// <summary>
/// Verifies X-Stockyard-Signature the way a supplier would, from the documented format alone
/// (t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;), without Stockyard's own code.
/// </summary>
public sealed class SignatureVerifier(string secret, TimeProvider timeProvider)
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public bool IsValid(string? header, string body)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        string? timestamp = null;
        string? signature = null;
        foreach (var part in header.Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "t")
            {
                timestamp = pair[1];
            }
            else if (pair[0] == "v1")
            {
                signature = pair[1];
            }
        }

        if (timestamp is null || signature is null
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var age = timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > Tolerance)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        byte[] actual;
        try
        {
            actual = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
