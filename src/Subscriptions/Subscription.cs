namespace Subscriptions;

public enum Plan
{
    Basic,
    Pro,
    Team,
}

public enum SubscriptionStatus
{
    Active,
    Cancelled,
}

public static class PlanPricing
{
    public static decimal MonthlyPrice(Plan plan) => plan switch
    {
        Plan.Basic => 29.90m,
        Plan.Pro => 79.90m,
        Plan.Team => 199.90m,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };
}

public sealed class Subscription
{
    private Subscription()
    {
    }

    public Guid Id { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public Plan Plan { get; private set; }

    public decimal MonthlyPrice { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static Subscription Start(string customerEmail, Plan plan, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        CustomerEmail = customerEmail,
        Plan = plan,
        MonthlyPrice = PlanPricing.MonthlyPrice(plan),
        Status = SubscriptionStatus.Active,
        StartedAt = now,
    };

    /// <summary>Returns false if it was already cancelled; cancelling twice must not emit a second event.</summary>
    public bool Cancel(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return false;
        }

        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
        return true;
    }
}
