using Outbox;

namespace TransactionalOutbox.UnitTests;

public class RetryPolicyTests
{
    private static readonly RetryPolicy Policy = new(MaxAttempts: 10, BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(1000, 30)]
    public void Delay_doubles_after_each_failure_up_to_the_cap(int failedAttempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Policy.DelayAfter(failedAttempts));

    [Fact]
    public void Delay_needs_at_least_one_failure() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy.DelayAfter(0));
}
