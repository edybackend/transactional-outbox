using System.Text.Json;
using Billing.Persistence;
using Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Subscriptions.Contracts;

namespace Billing;

/// <summary>
/// Idempotent consumer. The effect of a message and its inbox row are saved in one transaction, so a message is
/// either fully processed exactly once or not at all. A redelivery finds the inbox row and is skipped.
/// </summary>
public sealed partial class SubscriptionEventsHandler(
    BillingDbContext db,
    TimeProvider clock,
    ILogger<SubscriptionEventsHandler> logger) : IMessageHandler
{
    private const int SqliteConstraintViolation = 19;

    public async Task HandleAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (await db.Inbox.AnyAsync(m => m.MessageId == message.MessageId, cancellationToken))
        {
            LogDuplicate(message.Type, message.MessageId);
            return;
        }

        var now = clock.GetUtcNow();
        switch (message.Type)
        {
            case SubscriptionEventTypes.Created:
                OnCreated(Deserialize<SubscriptionCreated>(message), now);
                break;
            case SubscriptionEventTypes.Cancelled:
                await OnCancelledAsync(Deserialize<SubscriptionCancelled>(message), cancellationToken);
                break;
            default:
                LogUnknownType(message.Type, message.MessageId); // a newer producer: record it and move on
                break;
        }

        db.Inbox.Add(InboxMessage.For(message.MessageId, message.Type, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation })
        {
            // Two deliveries of the same message raced; the other one committed first. Nothing to do.
            LogDuplicate(message.Type, message.MessageId);
        }
    }

    private void OnCreated(SubscriptionCreated created, DateTimeOffset now)
    {
        db.Accounts.Add(BillingAccount.Open(created.SubscriptionId, created.CustomerEmail, created.Plan, created.StartedAt));
        db.Invoices.Add(Invoice.Issue(created.SubscriptionId, created.CustomerEmail, created.MonthlyPrice, now));
        LogInvoiceIssued(created.SubscriptionId, created.MonthlyPrice);
    }

    private async Task OnCancelledAsync(SubscriptionCancelled cancelled, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.SubscriptionId == cancelled.SubscriptionId, cancellationToken);
        if (account is null)
        {
            LogUnknownAccount(cancelled.SubscriptionId);
            return;
        }

        account.Close(cancelled.CancelledAt);
        var openInvoices = await db.Invoices
            .Where(i => i.SubscriptionId == cancelled.SubscriptionId && i.Status == InvoiceStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var invoice in openInvoices)
        {
            invoice.MarkVoid();
        }
    }

    private static T Deserialize<T>(MessageEnvelope message) =>
        JsonSerializer.Deserialize<T>(message.Payload, ContractJson.Options)
        ?? throw new JsonException($"Message {message.MessageId} has an empty {message.Type} payload.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped duplicate {Type} {MessageId}")]
    private partial void LogDuplicate(string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued invoice for subscription {SubscriptionId}: {Amount}")]
    private partial void LogInvoiceIssued(Guid subscriptionId, decimal amount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored unknown message type {Type} ({MessageId})")]
    private partial void LogUnknownType(string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cancellation for unknown billing account {SubscriptionId}; ignored")]
    private partial void LogUnknownAccount(Guid subscriptionId);
}
