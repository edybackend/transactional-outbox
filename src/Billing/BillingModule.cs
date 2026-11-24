using Billing.Persistence;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Billing;

public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Billing")
            ?? throw new InvalidOperationException("Connection string 'Billing' is missing.");
        services.AddDbContext<BillingDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IMessageHandler, SubscriptionEventsHandler>();
        services.AddScoped<BillingQueries>();
        return services;
    }

    public static async Task InitializeBillingDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var file = db.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(file) && Path.GetDirectoryName(Path.GetFullPath(file)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
