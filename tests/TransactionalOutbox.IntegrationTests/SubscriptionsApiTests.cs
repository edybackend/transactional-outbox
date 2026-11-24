using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace TransactionalOutbox.IntegrationTests;

/// <summary>End to end through the HTTP API with the in-memory transport: write → outbox → relay → broker → Billing.</summary>
public sealed class SubscriptionsApiTests : IDisposable
{
    private readonly OutboxApiFactory _factory = new();
    private readonly HttpClient _client;

    public SubscriptionsApiTests() => _client = _factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_subscription_reaches_billing_through_the_outbox()
    {
        var subscription = await _client.StartAsync("ana@example.com", "Team");

        Assert.Equal("Active", subscription.Status);
        Assert.Equal(199.90m, subscription.MonthlyPrice);
        await Eventually.TrueAsync(async () => (await _client.InvoicesAsync(subscription.Id)).Count == 1, "Billing issued the invoice");
        Assert.Equal(199.90m, (await _client.InvoicesAsync(subscription.Id))[0].Amount);
        Assert.Equal(new OutboxStatsDto(0, 1, 0), await _client.OutboxStatsAsync());
    }

    [Fact]
    public async Task During_a_broker_outage_writes_succeed_and_the_backlog_drains_when_it_returns()
    {
        await _client.SetBrokerAsync(available: false);

        var subscriptions = new List<SubscriptionDto>();
        for (var i = 0; i < 5; i++)
        {
            subscriptions.Add(await _client.StartAsync($"customer{i}@example.com", "Basic"));
        }

        await Eventually.TrueAsync(
            async () => (await _client.GetAsync<List<OutboxMessageDto>>("/api/outbox?status=Pending")).All(m => m.Attempts >= 2),
            "the relay retried every message");
        Assert.Equal(5, (await _client.OutboxStatsAsync()).Pending);
        Assert.Empty(await _client.InvoicesAsync());
        var pending = await _client.GetAsync<List<OutboxMessageDto>>("/api/outbox?status=Pending");
        Assert.All(pending, m => Assert.Contains("down", m.LastError, StringComparison.Ordinal));

        await _client.SetBrokerAsync(available: true);

        await Eventually.TrueAsync(async () => (await _client.InvoicesAsync()).Count == 5, "every subscription was billed");
        Assert.Equal(new OutboxStatsDto(0, 5, 0), await _client.OutboxStatsAsync());
        Assert.Equal(
            subscriptions.Select(s => s.Id).Order(),
            (await _client.InvoicesAsync()).Select(i => i.SubscriptionId).Order());
    }

    [Fact]
    public async Task Cancelling_voids_the_invoice_and_cancelling_twice_is_a_conflict()
    {
        var subscription = await _client.StartAsync("bruno@example.com", "Basic");

        var cancel = await _client.PostAsync($"/api/subscriptions/{subscription.Id}/cancel", null, Ct);
        var again = await _client.PostAsync($"/api/subscriptions/{subscription.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await Eventually.TrueAsync(
            async () => (await _client.InvoicesAsync(subscription.Id)) is [{ Status: "Void" }],
            "Billing voided the invoice after the cancellation");
        Assert.Equal(2, (await _client.OutboxStatsAsync()).Published); // created + cancelled, never a second cancellation
    }

    [Theory]
    [InlineData("not-an-email", "Pro", "customerEmail")]
    [InlineData("ana@example.com", "Gold", "plan")]
    [InlineData("ana@example.com", "1", "plan")]
    [InlineData("ana@example.com", "Basic,Pro", "plan")]
    public async Task Invalid_input_is_a_400_with_the_field(string email, string plan, string field)
    {
        var response = await _client.PostAsJsonAsync("/api/subscriptions", new { customerEmail = email, plan }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, (await _client.OutboxStatsAsync()).Pending + (await _client.OutboxStatsAsync()).Published);
    }

    [Fact]
    public async Task Malformed_json_is_a_400_not_a_500()
    {
        var response = await _client.PostAsync("/api/subscriptions", new StringContent("{bad", Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_ids_are_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/subscriptions/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/subscriptions/{Guid.NewGuid()}/cancel", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/outbox/{Guid.NewGuid()}/requeue", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_dead_lettered_messages_can_be_requeued()
    {
        await _client.StartAsync("carla@example.com");
        await Eventually.TrueAsync(async () => (await _client.OutboxStatsAsync()).Published == 1, "the message was published");
        var published = Assert.Single(await _client.GetAsync<List<OutboxMessageDto>>("/api/outbox"));

        var response = await _client.PostAsync($"/api/outbox/{published.MessageId}/requeue", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_api_reference_and_health_check_are_served()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/scalar", Ct)).StatusCode);
        Assert.Equal(new BrokerDto("InMemory", true), await _client.GetAsync<BrokerDto>("/api/broker"));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record BrokerDto(string Transport, bool Available);
}
