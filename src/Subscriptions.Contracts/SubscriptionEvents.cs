using System.Text.Json;

namespace Subscriptions.Contracts;

/// <summary>
/// The integration events other modules can rely on. A published message is a contract: change it only
/// by adding optional fields, or by introducing a new type with a new version suffix.
/// </summary>
public static class SubscriptionEventTypes
{
    public const string Created = "subscription.created.v1";
    public const string Cancelled = "subscription.cancelled.v1";
}

public sealed record SubscriptionCreated(
    Guid SubscriptionId,
    string CustomerEmail,
    string Plan,
    decimal MonthlyPrice,
    DateTimeOffset StartedAt);

public sealed record SubscriptionCancelled(Guid SubscriptionId, DateTimeOffset CancelledAt);

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
