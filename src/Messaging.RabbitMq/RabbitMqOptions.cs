namespace Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";

    public string ExchangeName { get; set; } = "subscriptions";

    /// <summary>The consumer's queue. It is bound to every routing key (<c>#</c>) of the exchange.</summary>
    public string QueueName { get; set; } = "billing.subscriptions";

    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>How long the consumer waits before trying to connect again when the broker is down at startup.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
}
