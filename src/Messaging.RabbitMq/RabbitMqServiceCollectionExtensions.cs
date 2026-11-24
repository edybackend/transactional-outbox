using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>Registers RabbitMQ as publisher and starts consuming the queue into <see cref="IMessageHandler"/>.</summary>
    public static IServiceCollection AddRabbitMqTransport(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IBrokerStatus>(sp => sp.GetRequiredService<RabbitMqConnection>());
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddHostedService<RabbitMqConsumer>();
        return services;
    }
}
