using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Outbox;
using Subscriptions.Persistence;

namespace Subscriptions;

public static class SubscriptionsModule
{
    public static IServiceCollection AddSubscriptionsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Subscriptions")
            ?? throw new InvalidOperationException("Connection string 'Subscriptions' is missing.");
        services.AddDbContext<SubscriptionsDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<SubscriptionService>();
        services.AddOutbox<SubscriptionsDbContext>(configuration);
        return services;
    }

    /// <summary>Creates the database if needed. WAL lets the API write while the relay reads and updates the outbox.</summary>
    public static async Task InitializeSubscriptionsDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        await PrepareSqliteAsync(db);
    }

    internal static async Task PrepareSqliteAsync(DbContext db)
    {
        var file = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(file) && Path.GetDirectoryName(Path.GetFullPath(file)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
