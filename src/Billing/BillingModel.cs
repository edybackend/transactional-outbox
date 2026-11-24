namespace Billing;

public enum BillingAccountStatus
{
    Active,
    Closed,
}

public enum InvoiceStatus
{
    Open,
    Void,
}

/// <summary>Billing's own copy of what it needs about a subscription. It never reads the Subscriptions database.</summary>
public sealed class BillingAccount
{
    private BillingAccount()
    {
    }

    public Guid SubscriptionId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public string Plan { get; private set; } = string.Empty;

    public BillingAccountStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static BillingAccount Open(Guid subscriptionId, string customerEmail, string plan, DateTimeOffset openedAt) => new()
    {
        SubscriptionId = subscriptionId,
        CustomerEmail = customerEmail,
        Plan = plan,
        Status = BillingAccountStatus.Active,
        OpenedAt = openedAt,
    };

    public void Close(DateTimeOffset closedAt)
    {
        Status = BillingAccountStatus.Closed;
        ClosedAt = closedAt;
    }
}

public sealed class Invoice
{
    private Invoice()
    {
    }

    public Guid Id { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public static Invoice Issue(Guid subscriptionId, string customerEmail, decimal amount, DateTimeOffset issuedAt) => new()
    {
        Id = Guid.CreateVersion7(issuedAt),
        SubscriptionId = subscriptionId,
        CustomerEmail = customerEmail,
        Amount = amount,
        Status = InvoiceStatus.Open,
        IssuedAt = issuedAt,
    };

    public void MarkVoid() => Status = InvoiceStatus.Void;
}

/// <summary>A message Billing has already processed. Its primary key is what makes redeliveries harmless.</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public Guid MessageId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage For(Guid messageId, string type, DateTimeOffset processedAt) =>
        new() { MessageId = messageId, Type = type, ProcessedAt = processedAt };
}
