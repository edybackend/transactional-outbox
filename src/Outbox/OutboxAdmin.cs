using Microsoft.EntityFrameworkCore;

namespace Outbox;

public enum RequeueOutcome
{
    Requeued,
    NotFound,
    NotDeadLettered,
}

public sealed record OutboxMessageView(
    long Sequence,
    Guid MessageId,
    string Type,
    string StreamKey,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset OccurredAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? PublishedAt,
    string? LastError);

public sealed record OutboxStats(int Pending, int Published, int DeadLettered);

/// <summary>Read side and operator actions for the outbox of one module.</summary>
public sealed class OutboxAdmin<TContext>(TContext db, TimeProvider clock)
    where TContext : DbContext
{
    public async Task<OutboxStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Set<OutboxMessage>()
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int CountOf(OutboxStatus status) => counts.SingleOrDefault(c => c.Status == status)?.Count ?? 0;
        return new OutboxStats(CountOf(OutboxStatus.Pending), CountOf(OutboxStatus.Published), CountOf(OutboxStatus.DeadLettered));
    }

    /// <summary>Most recent first.</summary>
    public async Task<IReadOnlyList<OutboxMessageView>> ListAsync(OutboxStatus? status, int take, CancellationToken cancellationToken)
    {
        var query = db.Set<OutboxMessage>().AsNoTracking();
        if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        return await query
            .OrderByDescending(m => m.Sequence)
            .Take(take)
            .Select(m => new OutboxMessageView(
                m.Sequence, m.MessageId, m.Type, m.StreamKey, m.Status, m.Attempts, m.OccurredAt, m.NextAttemptAt, m.PublishedAt, m.LastError))
            .ToListAsync(cancellationToken);
    }

    public async Task<RequeueOutcome> RequeueAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Set<OutboxMessage>().SingleOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);
        if (message is null)
        {
            return RequeueOutcome.NotFound;
        }

        if (!message.Requeue(clock.GetUtcNow()))
        {
            return RequeueOutcome.NotDeadLettered;
        }

        await db.SaveChangesAsync(cancellationToken);
        return RequeueOutcome.Requeued;
    }
}
