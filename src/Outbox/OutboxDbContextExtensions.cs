using Microsoft.EntityFrameworkCore;

namespace Outbox;

public static class OutboxDbContextExtensions
{
    /// <summary>
    /// Stages a message in the outbox. Nothing is sent here: the message is saved by the caller's next
    /// <c>SaveChanges</c>, in the same transaction as the business change.
    /// </summary>
    public static OutboxMessage AddToOutbox(this DbContext db, string type, string streamKey, string payload, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        var message = OutboxMessage.Create(type, streamKey, payload, now);
        db.Set<OutboxMessage>().Add(message);
        return message;
    }
}
