namespace Stockyard.Domain.Webhooks;

/// <summary>
/// Exponential backoff for webhook deliveries: the first retry waits the initial delay, each
/// following one doubles it up to the cap, and the delivery is abandoned after the last attempt.
/// </summary>
public sealed class WebhookRetryPolicy
{
    public WebhookRetryPolicy(TimeSpan initialDelay, TimeSpan maxDelay, int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, initialDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
        MaxAttempts = maxAttempts;
    }

    /// <summary>1 s doubling to a 10 minute cap, 8 attempts.</summary>
    public static WebhookRetryPolicy Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), 8);

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaxDelay { get; }

    public int MaxAttempts { get; }

    /// <summary>True while another attempt is allowed after <paramref name="attemptsMade"/> attempts.</summary>
    public bool CanRetry(int attemptsMade) => attemptsMade < MaxAttempts;

    /// <summary>The wait before the next attempt, given how many attempts have failed so far.</summary>
    public TimeSpan DelayAfter(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);

        var doublings = failedAttempts - 1;
        if (doublings >= 31)
        {
            return MaxDelay;
        }

        var ticks = InitialDelay.Ticks * (double)(1L << doublings);
        return ticks >= MaxDelay.Ticks ? MaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
