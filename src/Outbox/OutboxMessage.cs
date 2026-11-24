using Messaging;

namespace Outbox;

public enum OutboxStatus
{
    Pending,
    Published,
    DeadLettered,
}

/// <summary>
/// A message waiting to be published. It is written in the same transaction as the business change, so
/// either both exist or neither does. The relay publishes it later and records every attempt.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    /// <summary>Insertion order. Used to publish the messages of one stream in the order they were written.</summary>
    public long Sequence { get; private set; }

    public Guid MessageId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>The entity the message is about. Messages with the same key are published in order.</summary>
    public string StreamKey { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Set while a relay owns the message; another relay can take it over once the lease expired.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public Guid? LockId { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string streamKey, string payload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);
        ArgumentNullException.ThrowIfNull(payload);
        return new OutboxMessage
        {
            MessageId = Guid.CreateVersion7(now),
            Type = type,
            StreamKey = streamKey,
            Payload = payload,
            OccurredAt = now,
            Status = OutboxStatus.Pending,
            NextAttemptAt = now,
        };
    }

    public MessageEnvelope ToEnvelope() => new(MessageId, Type, StreamKey, Payload, OccurredAt);

    public void MarkPublished(DateTimeOffset now)
    {
        Status = OutboxStatus.Published;
        PublishedAt = now;
        Attempts++;
        LastError = null;
        ReleaseLock();
    }

    /// <summary>Schedules the next attempt with exponential backoff, or dead-letters the message after the last one.</summary>
    public void MarkFailed(string error, DateTimeOffset now, RetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Attempts++;
        LastError = error.Length <= 500 ? error : error[..500];
        if (Attempts >= policy.MaxAttempts)
        {
            Status = OutboxStatus.DeadLettered;
        }
        else
        {
            NextAttemptAt = now + policy.DelayAfter(Attempts);
        }

        ReleaseLock();
    }

    /// <summary>An operator decided to try a dead-lettered message again (after fixing the cause).</summary>
    public bool Requeue(DateTimeOffset now)
    {
        if (Status != OutboxStatus.DeadLettered)
        {
            return false;
        }

        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        return true;
    }

    private void ReleaseLock()
    {
        LockedUntil = null;
        LockId = null;
    }
}
