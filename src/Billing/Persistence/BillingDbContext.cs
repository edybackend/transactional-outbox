using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Billing.Persistence;

/// <summary>The Billing database — separate from Subscriptions, as it would be in a separate service.</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<BillingAccount> Accounts => Set<BillingAccount>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingAccount>(account =>
        {
            account.ToTable("billing_accounts");
            account.HasKey(a => a.SubscriptionId);
            account.Property(a => a.CustomerEmail).HasMaxLength(254);
            account.Property(a => a.Plan).HasMaxLength(20);
            account.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTable("invoices");
            invoice.HasKey(i => i.Id);
            invoice.HasIndex(i => i.SubscriptionId);
            invoice.Property(i => i.CustomerEmail).HasMaxLength(254);
            invoice.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_messages");
            inbox.HasKey(m => m.MessageId);
            inbox.Property(m => m.Type).HasMaxLength(200);
        });
    }
}
