using Subscriptions;

namespace TransactionalOutbox.UnitTests;

public class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Plan.Basic, 29.90)]
    [InlineData(Plan.Pro, 79.90)]
    [InlineData(Plan.Team, 199.90)]
    public void A_subscription_starts_active_with_the_plan_price(Plan plan, decimal price)
    {
        var subscription = Subscription.Start("ana@example.com", plan, Now);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(price, subscription.MonthlyPrice);
        Assert.Equal(Now, subscription.StartedAt);
    }

    [Fact]
    public void Cancelling_twice_is_refused()
    {
        var subscription = Subscription.Start("ana@example.com", Plan.Pro, Now);

        Assert.True(subscription.Cancel(Now.AddDays(3)));
        Assert.False(subscription.Cancel(Now.AddDays(4)));
        Assert.Equal(Now.AddDays(3), subscription.CancelledAt);
    }
}
