using Stockyard.Domain.Webhooks;

namespace Stockyard.UnitTests.Webhooks;

public sealed class WebhookSignatureTests
{
    private const string Secret = "local-signing-secret";
    private const string Body = "{\"orderId\":\"7d3c\"}";
    private const long Timestamp = 1786550400;

    // Computed independently: HMAC-SHA256(secret, "1786550400." + body), hex.
    private const string KnownSignature = "403d6acc1d9eba683359a6063c9be56d301de718aa021a392dec8e95ee3d94c3";

    private static readonly DateTimeOffset SignedAt = DateTimeOffset.FromUnixTimeSeconds(Timestamp);

    [Fact]
    public void ComputeHexMatchesAKnownVector()
    {
        WebhookSignature.ComputeHex(Secret, Timestamp, Body).ShouldBe(KnownSignature);
    }

    [Fact]
    public void CreateHeaderUsesTheDocumentedFormat()
    {
        WebhookSignature.CreateHeader(Secret, Timestamp, Body).ShouldBe($"t={Timestamp},v1={KnownSignature}");
    }

    [Fact]
    public void VerifyAcceptsAFreshSignature()
    {
        var header = WebhookSignature.CreateHeader(Secret, Timestamp, Body);

        WebhookSignature.Verify(Secret, header, Body, SignedAt.AddSeconds(30), TimeSpan.FromMinutes(5)).ShouldBeTrue();
    }

    [Fact]
    public void VerifyRejectsATamperedBody()
    {
        var header = WebhookSignature.CreateHeader(Secret, Timestamp, Body);

        WebhookSignature.Verify(Secret, header, Body.Replace("7d3c", "7d3d", StringComparison.Ordinal), SignedAt, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Fact]
    public void VerifyRejectsTheWrongSecret()
    {
        var header = WebhookSignature.CreateHeader(Secret, Timestamp, Body);

        WebhookSignature.Verify("another-secret", header, Body, SignedAt, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Fact]
    public void VerifyRejectsAnOldTimestamp()
    {
        var header = WebhookSignature.CreateHeader(Secret, Timestamp, Body);

        WebhookSignature.Verify(Secret, header, Body, SignedAt.AddMinutes(6), TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=,v1=abc")]
    [InlineData("t=-5,v1=abc")]
    [InlineData("t=1786550400")]
    public void TryParseRejectsMalformedHeaders(string? header)
    {
        WebhookSignature.TryParse(header, out _, out _).ShouldBeFalse();
    }
}
