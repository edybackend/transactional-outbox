using Subscriptions;

namespace TransactionalOutbox.Api;

/// <summary>A few subscriptions created through the normal use cases, so the outbox and the invoices are not empty on first run.</summary>
internal static class SampleData
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        if ((await subscriptions.ListAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        await subscriptions.StartAsync(new StartSubscription("ana@example.com", "Pro"), cancellationToken);
        await subscriptions.StartAsync(new StartSubscription("bruno@example.com", "Basic"), cancellationToken);
        var team = await subscriptions.StartAsync(new StartSubscription("carla@example.com", "Team"), cancellationToken);
        await subscriptions.CancelAsync(team.Value!.Id, cancellationToken);
    }
}
