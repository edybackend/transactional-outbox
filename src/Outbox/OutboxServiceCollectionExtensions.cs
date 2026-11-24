using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox of <typeparamref name="TContext"/> and the relay that publishes it.
    /// The context must call <see cref="OutboxModelBuilderExtensions.AddOutbox"/> in <c>OnModelCreating</c>.
    /// </summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddScoped<OutboxDispatcher<TContext>>();
        services.AddScoped<OutboxAdmin<TContext>>();
        services.AddHostedService<OutboxRelay<TContext>>();
        return services;
    }
}
