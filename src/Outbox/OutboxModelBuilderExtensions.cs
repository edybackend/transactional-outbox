using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>Adds the <c>outbox_messages</c> table to a module's model, next to its own tables (same database, same transaction).</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.HasKey(m => m.Sequence);
            message.Property(m => m.Sequence).ValueGeneratedOnAdd();
            message.HasIndex(m => m.MessageId).IsUnique();
            message.Property(m => m.Type).HasMaxLength(200);
            message.Property(m => m.StreamKey).HasMaxLength(200);
            message.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            message.Property(m => m.LastError).HasMaxLength(500);

            // Stored as sortable integers so SQLite can compare them in the claim query.
            message.Property(m => m.OccurredAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            message.Property(m => m.NextAttemptAt).HasConversion<DateTimeOffsetToBinaryConverter>();
            message.Property(m => m.LockedUntil).HasConversion<DateTimeOffsetToBinaryConverter>();
            message.Property(m => m.PublishedAt).HasConversion<DateTimeOffsetToBinaryConverter>();

            message.HasIndex(m => new { m.Status, m.NextAttemptAt });
            message.HasIndex(m => new { m.StreamKey, m.Sequence });
        });
        return modelBuilder;
    }
}
