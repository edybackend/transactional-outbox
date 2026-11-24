using Messaging.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>Registers the in-process broker as publisher and starts delivering to <see cref="IMessageHandler"/>.</summary>
    public static IServiceCollection AddInMemoryTransport(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryBroker>();
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<InMemoryBroker>());
        services.AddSingleton<IBrokerStatus>(sp => sp.GetRequiredService<InMemoryBroker>());
        services.AddHostedService<InMemoryConsumer>();
        return services;
    }
}
