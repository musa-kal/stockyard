using Stockyard.Domain.Webhooks;

namespace Stockyard.UnitTests.Webhooks;

public sealed class WebhookRetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 64)]
    public void DelayDoublesFromOneSecond(int failedAttempts, int expectedSeconds)
    {
        WebhookRetryPolicy.Default.DelayAfter(failedAttempts).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void DelayIsCappedAtTenMinutes(int failedAttempts)
    {
        WebhookRetryPolicy.Default.DelayAfter(failedAttempts).ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void DefaultAllowsEightAttempts()
    {
        WebhookRetryPolicy.Default.CanRetry(7).ShouldBeTrue();
        WebhookRetryPolicy.Default.CanRetry(8).ShouldBeFalse();
    }

    [Fact]
    public void DelayAfterRejectsZeroAttempts()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WebhookRetryPolicy.Default.DelayAfter(0));
    }

    [Fact]
    public void ConstructorRejectsACapBelowTheInitialDelay()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new WebhookRetryPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), 3));
    }
}
