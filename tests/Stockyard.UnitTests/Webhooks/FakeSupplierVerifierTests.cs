using FakeSupplier;
using Microsoft.Extensions.Time.Testing;
using Stockyard.Domain.Webhooks;

namespace Stockyard.UnitTests.Webhooks;

/// <summary>The fake supplier verifies from the documented format; it must agree with the signer.</summary>
public sealed class FakeSupplierVerifierTests
{
    private const string Secret = "shared-local-secret";
    private const string Body = """{"orderId":"5B1D","number":"PO-001000"}""";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AcceptsWhatTheNotifierSigns()
    {
        var header = WebhookSignature.CreateHeader(Secret, time.GetUtcNow().ToUnixTimeSeconds(), Body);

        new SignatureVerifier(Secret, time).IsValid(header, Body).ShouldBeTrue();
    }

    [Fact]
    public void RejectsAnotherSecretOrABodyChange()
    {
        var header = WebhookSignature.CreateHeader(Secret, time.GetUtcNow().ToUnixTimeSeconds(), Body);

        new SignatureVerifier("other-secret", time).IsValid(header, Body).ShouldBeFalse();
        new SignatureVerifier(Secret, time).IsValid(header, Body + " ").ShouldBeFalse();
    }

    [Fact]
    public void RejectsASignatureOlderThanTheTolerance()
    {
        var header = WebhookSignature.CreateHeader(Secret, time.GetUtcNow().ToUnixTimeSeconds(), Body);
        time.Advance(SignatureVerifier.Tolerance + TimeSpan.FromSeconds(1));

        new SignatureVerifier(Secret, time).IsValid(header, Body).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t=abc,v1=00")]
    [InlineData("t=1789135200,v1=not-hex")]
    [InlineData("v1=00")]
    public void RejectsMalformedHeaders(string? header)
    {
        new SignatureVerifier(Secret, time).IsValid(header, Body).ShouldBeFalse();
    }
}
