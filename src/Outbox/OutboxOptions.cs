namespace Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 20;

    /// <summary>How long the relay sleeps when there was nothing to publish.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a claimed batch belongs to one relay. If it crashes, another relay takes over after this.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 25;

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    public RetryPolicy RetryPolicy => new(MaxAttempts, BaseBackoff, MaxBackoff);
}
