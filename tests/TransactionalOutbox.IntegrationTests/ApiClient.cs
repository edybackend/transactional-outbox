using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransactionalOutbox.IntegrationTests;

public sealed record SubscriptionDto(Guid Id, string CustomerEmail, string Plan, decimal MonthlyPrice, string Status);

public sealed record InvoiceDto(Guid Id, Guid SubscriptionId, decimal Amount, string Status);

public sealed record OutboxStatsDto(int Pending, int Published, int DeadLettered);

public sealed record OutboxMessageDto(Guid MessageId, string Type, string StreamKey, string Status, int Attempts, string? LastError);

public static class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<SubscriptionDto> StartAsync(this HttpClient client, string email, string plan = "Pro")
    {
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { customerEmail = email, plan }, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionDto>(Json, TestContext.Current.CancellationToken))!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url) =>
        (await client.GetFromJsonAsync<T>(url, Json, TestContext.Current.CancellationToken))!;

    public static Task<List<InvoiceDto>> InvoicesAsync(this HttpClient client, Guid? subscriptionId = null) =>
        client.GetAsync<List<InvoiceDto>>(subscriptionId is null ? "/api/invoices" : $"/api/invoices?subscriptionId={subscriptionId}");

    public static Task<OutboxStatsDto> OutboxStatsAsync(this HttpClient client) =>
        client.GetAsync<OutboxStatsDto>("/api/outbox/stats");

    public static async Task SetBrokerAsync(this HttpClient client, bool available)
    {
        var response = await client.PutAsJsonAsync("/api/broker", new { available }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
