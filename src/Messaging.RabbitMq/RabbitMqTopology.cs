using RabbitMQ.Client;

namespace Messaging.RabbitMq;

/// <summary>Idempotent declarations, so the broker needs no manual setup.</summary>
internal static class RabbitMqTopology
{
    public static Task DeclareExchangeAsync(IChannel channel, RabbitMqOptions options, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);

    /// <summary>The consumer queue and its dead-letter queue: a message the handler rejects goes there instead of looping.</summary>
    public static async Task DeclareQueueAsync(IChannel channel, RabbitMqOptions options, CancellationToken ct)
    {
        await DeclareExchangeAsync(channel, options, ct);

        var deadLetterExchange = $"{options.ExchangeName}.dlx";
        var deadLetterQueue = $"{options.QueueName}.dlq";
        await channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, routingKey: string.Empty, cancellationToken: ct);

        var arguments = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = deadLetterExchange };
        await channel.QueueDeclareAsync(options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments, cancellationToken: ct);
        await channel.QueueBindAsync(options.QueueName, options.ExchangeName, routingKey: "#", cancellationToken: ct);
    }
}
