using System.Net;
using System.Net.Http.Json;

namespace TransactionalOutbox.IntegrationTests;

public sealed class RabbitMqTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_rabbitmq_down_from_the_start_the_api_boots_and_keeps_the_messages()
    {
        using var factory = new UnreachableRabbitMqFactory();
        using var client = factory.CreateClient();

        var subscription = await client.StartAsync("ana@example.com");

        await Eventually.TrueAsync(
            async () => (await client.GetAsync<List<OutboxMessageDto>>("/api/outbox")) is [{ Attempts: >= 2, Status: "Pending" }],
            "the relay retried against the unreachable broker");
        var message = Assert.Single(await client.GetAsync<List<OutboxMessageDto>>("/api/outbox"));
        Assert.Equal(subscription.Id.ToString(), message.StreamKey);
        Assert.Contains("not reachable", message.LastError, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);

        var simulate = await client.PutAsJsonAsync("/api/broker", new { available = false }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, simulate.StatusCode); // outages are real here: stop the container
    }

    [Fact]
    public async Task Through_a_real_rabbitmq_a_subscription_is_billed_once()
    {
        Assert.SkipWhen(RealRabbitMqFactory.Url is null, "Set RABBITMQ_URL to run against a real broker (CI does).");
        using var factory = new RealRabbitMqFactory();
        using var client = factory.CreateClient();

        var first = await client.StartAsync("ana@example.com", "Pro");
        var second = await client.StartAsync("bruno@example.com", "Basic");
        var cancel = await client.PostAsync($"/api/subscriptions/{second.Id}/cancel", null, Ct);
        cancel.EnsureSuccessStatusCode();

        await Eventually.TrueAsync(
            async () => (await client.InvoicesAsync()) is { Count: 2 } invoices && invoices.Any(i => i.Status == "Void"),
            "both invoices arrived through RabbitMQ and the cancellation voided one",
            timeoutMs: 30_000);
        var invoices = await client.InvoicesAsync();
        Assert.Equal("Open", invoices.Single(i => i.SubscriptionId == first.Id).Status);
        Assert.Equal("Void", invoices.Single(i => i.SubscriptionId == second.Id).Status);
        Assert.Equal(new OutboxStatsDto(0, 3, 0), await client.OutboxStatsAsync());
    }
}
