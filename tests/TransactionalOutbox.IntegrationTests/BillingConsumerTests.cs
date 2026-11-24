using System.Text.Json;
using Billing;
using Billing.Persistence;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Subscriptions.Contracts;

namespace TransactionalOutbox.IntegrationTests;

/// <summary>The inbox makes the consumer idempotent: at-least-once delivery, effectively-once processing.</summary>
public sealed class BillingConsumerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDatabase _database = new();
    private readonly MovableClock _clock = new(Now);

    public BillingConsumerTests()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_created_subscription_gets_its_first_invoice()
    {
        var subscriptionId = Guid.NewGuid();

        await HandleAsync(Created(subscriptionId, 79.90m));

        var invoice = Assert.Single(await InvoicesAsync());
        Assert.Equal(subscriptionId, invoice.SubscriptionId);
        Assert.Equal(79.90m, invoice.Amount);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);
    }

    [Fact]
    public async Task Delivering_the_same_message_twice_issues_one_invoice()
    {
        var message = Created(Guid.NewGuid(), 29.90m);

        await HandleAsync(message);
        await HandleAsync(message);

        Assert.Single(await InvoicesAsync());
    }

    [Fact]
    public async Task Concurrent_deliveries_of_the_same_message_issue_one_invoice()
    {
        var message = Created(Guid.NewGuid(), 29.90m);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => HandleAsync(message)));

        Assert.Single(await InvoicesAsync());
        await using var db = NewContext();
        Assert.Equal(1, await db.Inbox.CountAsync(Ct));
    }

    [Fact]
    public async Task A_cancellation_closes_the_account_and_voids_open_invoices()
    {
        var subscriptionId = Guid.NewGuid();
        await HandleAsync(Created(subscriptionId, 199.90m));

        await HandleAsync(Envelope(SubscriptionEventTypes.Cancelled, subscriptionId, new SubscriptionCancelled(subscriptionId, Now.AddDays(2))));

        Assert.Equal(InvoiceStatus.Void, Assert.Single(await InvoicesAsync()).Status);
        await using var db = NewContext();
        var account = await db.Accounts.SingleAsync(Ct);
        Assert.Equal(BillingAccountStatus.Closed, account.Status);
        Assert.Equal(Now.AddDays(2), account.ClosedAt);
    }

    [Fact]
    public async Task A_cancellation_for_an_unknown_account_is_recorded_and_ignored()
    {
        var subscriptionId = Guid.NewGuid();

        await HandleAsync(Envelope(SubscriptionEventTypes.Cancelled, subscriptionId, new SubscriptionCancelled(subscriptionId, Now)));

        await using var db = NewContext();
        Assert.Equal(1, await db.Inbox.CountAsync(Ct));
        Assert.Empty(await InvoicesAsync());
    }

    [Fact]
    public async Task A_message_that_fails_leaves_nothing_behind_so_a_redelivery_processes_it()
    {
        var subscriptionId = Guid.NewGuid();
        var broken = new MessageEnvelope(Guid.NewGuid(), SubscriptionEventTypes.Created, subscriptionId.ToString(), "{ not json", Now);

        await Assert.ThrowsAnyAsync<JsonException>(() => HandleAsync(broken));

        await using var db = NewContext();
        Assert.Equal(0, await db.Inbox.CountAsync(Ct));
        Assert.Empty(await InvoicesAsync());
    }

    public void Dispose() => _database.Dispose();

    private static MessageEnvelope Created(Guid subscriptionId, decimal price) =>
        Envelope(SubscriptionEventTypes.Created, subscriptionId, new SubscriptionCreated(subscriptionId, "ana@example.com", "Pro", price, Now));

    private static MessageEnvelope Envelope<T>(string type, Guid subscriptionId, T payload) =>
        new(Guid.CreateVersion7(), type, subscriptionId.ToString(), JsonSerializer.Serialize(payload, ContractJson.Options), Now);

    private BillingDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(_database.ConnectionString).Options);

    private async Task HandleAsync(MessageEnvelope message)
    {
        await using var db = NewContext();
        await new SubscriptionEventsHandler(db, _clock, NullLogger<SubscriptionEventsHandler>.Instance).HandleAsync(message, Ct);
    }

    private async Task<List<Invoice>> InvoicesAsync()
    {
        await using var db = NewContext();
        return await db.Invoices.AsNoTracking().ToListAsync(Ct);
    }
}
