using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Outbox;

namespace Subscriptions.Persistence;

/// <summary>The Subscriptions database. Its outbox lives here too, which is what makes the write atomic.</summary>
public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options) : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subscription>(subscription =>
        {
            subscription.ToTable("subscriptions");
            subscription.HasKey(s => s.Id);
            subscription.Property(s => s.CustomerEmail).HasMaxLength(254);
            subscription.Property(s => s.Plan).HasConversion<string>().HasMaxLength(20);
            subscription.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.AddOutbox();
    }
}
