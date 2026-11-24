namespace Outbox;

/// <summary>Exponential backoff: <c>baseDelay × 2^(attempt−1)</c>, capped at <see cref="MaxDelay"/>.</summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)
{
    public TimeSpan DelayAfter(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        var exponent = Math.Min(failedAttempts - 1, 30); // 2^30 × 1 ms is already far above any sane cap
        var delay = BaseDelay * Math.Pow(2, exponent);
        return delay < MaxDelay ? delay : MaxDelay;
    }
}
