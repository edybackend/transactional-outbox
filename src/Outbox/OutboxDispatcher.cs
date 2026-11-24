using Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Outbox;

/// <summary>
/// One pass of the relay: claim a batch, publish it, record the outcome.
/// <list type="bullet">
/// <item>The claim is a single <c>UPDATE</c> that sets a lease, so two relays never take the same message.</item>
/// <item>Only the oldest unpublished message of each stream is eligible, so a stream is published in order
/// and a failing message holds back only its own stream.</item>
/// <item>Each outcome is saved right away: if the process dies mid-batch, the finished messages stay finished.</item>
/// </list>
/// A crash after the broker confirmed but before the save means the message is sent again: delivery is
/// at-least-once, and consumers deduplicate by <see cref="MessageEnvelope.MessageId"/>.
/// </summary>
public sealed partial class OutboxDispatcher<TContext>(
    TContext db,
    IMessagePublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher<TContext>> logger)
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>Returns how many messages were processed (published or failed). Zero means there was nothing to do.</summary>
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var batch = await ClaimBatchAsync(cancellationToken);
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message.ToEnvelope(), cancellationToken);
                message.MarkPublished(clock.GetUtcNow());
                LogPublished(message.Type, message.MessageId, message.Attempts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.MarkFailed(ex.Message, clock.GetUtcNow(), _options.RetryPolicy);
                if (message.Status == OutboxStatus.DeadLettered)
                {
                    LogDeadLettered(message.Type, message.MessageId, message.Attempts, ex.Message);
                }
                else
                {
                    LogPublishFailed(message.Type, message.MessageId, message.Attempts, message.NextAttemptAt, ex.Message);
                }
            }

            await db.SaveChangesAsync(CancellationToken.None); // record the outcome even if shutdown was requested
        }

        return batch.Count;
    }

    private async Task<List<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lockId = Guid.CreateVersion7(now);
        var lockedUntil = now + _options.LeaseDuration;
        var messages = db.Set<OutboxMessage>();

        var claimed = await messages
            .Where(m => m.Status == OutboxStatus.Pending
                && m.NextAttemptAt <= now
                && (m.LockedUntil == null || m.LockedUntil < now))
            .Where(m => !messages.Any(earlier => earlier.StreamKey == m.StreamKey
                && earlier.Sequence < m.Sequence
                && earlier.Status != OutboxStatus.Published))
            .OrderBy(m => m.Sequence)
            .Take(_options.BatchSize)
            .ExecuteUpdateAsync(
                set => set.SetProperty(m => m.LockId, lockId).SetProperty(m => m.LockedUntil, lockedUntil),
                cancellationToken);

        return claimed == 0
            ? []
            : await messages.Where(m => m.LockId == lockId).OrderBy(m => m.Sequence).ToListAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {Type} {MessageId} (attempt {Attempt})")]
    private partial void LogPublished(string type, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing {Type} {MessageId} failed (attempt {Attempt}); next try at {NextAttemptAt:O}: {Reason}")]
    private partial void LogPublishFailed(string type, Guid messageId, int attempt, DateTimeOffset nextAttemptAt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettered {Type} {MessageId} after {Attempts} attempts: {Reason}")]
    private partial void LogDeadLettered(string type, Guid messageId, int attempts, string reason);
}
