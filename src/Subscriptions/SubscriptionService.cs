using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Outbox;
using Subscriptions.Contracts;
using Subscriptions.Persistence;

namespace Subscriptions;

public sealed record StartSubscription(string? CustomerEmail, string? Plan);

public sealed record SubscriptionView(
    Guid Id,
    string CustomerEmail,
    Plan Plan,
    decimal MonthlyPrice,
    SubscriptionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CancelledAt);

/// <summary>
/// Use cases of the Subscriptions module. Every change saves the entity and its integration event with one
/// <c>SaveChanges</c> — one transaction. The module never calls the broker; the outbox relay does.
/// </summary>
public sealed class SubscriptionService(SubscriptionsDbContext db, TimeProvider clock)
{
    public async Task<Result<SubscriptionView>> StartAsync(StartSubscription command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new Dictionary<string, string[]>();
        var email = command.CustomerEmail?.Trim() ?? string.Empty;
        if (email.Length is 0 or > 254 || !MailAddress.TryCreate(email, out _))
        {
            errors["customerEmail"] = ["A valid email address is required."];
        }

        // Match the name exactly: Enum.TryParse would also accept "1" or "Basic,Pro".
        var planName = Enum.GetNames<Plan>().FirstOrDefault(name => string.Equals(name, command.Plan?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (planName is null)
        {
            errors["plan"] = [$"Plan must be one of: {string.Join(", ", Enum.GetNames<Plan>())}."];
        }

        if (errors.Count > 0)
        {
            return new Failure(FailureKind.Validation, "The subscription is invalid.", errors);
        }

        var now = clock.GetUtcNow();
        var subscription = Subscription.Start(email, Enum.Parse<Plan>(planName!), now);
        db.Subscriptions.Add(subscription);
        db.AddToOutbox(
            SubscriptionEventTypes.Created,
            streamKey: subscription.Id.ToString(),
            JsonSerializer.Serialize(
                new SubscriptionCreated(subscription.Id, subscription.CustomerEmail, subscription.Plan.ToString(), subscription.MonthlyPrice, now),
                ContractJson.Options),
            now);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(subscription);
    }

    public async Task<Result<SubscriptionView>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
        {
            return NotFound(id);
        }

        var now = clock.GetUtcNow();
        if (!subscription.Cancel(now))
        {
            return new Failure(FailureKind.Conflict, "The subscription is already cancelled.");
        }

        db.AddToOutbox(
            SubscriptionEventTypes.Cancelled,
            streamKey: subscription.Id.ToString(),
            JsonSerializer.Serialize(new SubscriptionCancelled(subscription.Id, now), ContractJson.Options),
            now);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(subscription);
    }

    public async Task<Result<SubscriptionView>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return subscription is null ? NotFound(id) : ToView(subscription);
    }

    /// <summary>Newest first (ids are version 7 GUIDs, ordered by creation time).</summary>
    public async Task<IReadOnlyList<SubscriptionView>> ListAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await db.Subscriptions.AsNoTracking().OrderByDescending(s => s.StartedAt).Take(100).ToListAsync(cancellationToken);
        return [.. subscriptions.Select(ToView)];
    }

    private static Failure NotFound(Guid id) =>
        new(FailureKind.NotFound, $"Subscription {id} was not found.");

    private static SubscriptionView ToView(Subscription s) =>
        new(s.Id, s.CustomerEmail, s.Plan, s.MonthlyPrice, s.Status, s.StartedAt, s.CancelledAt);
}
