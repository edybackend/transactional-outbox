using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Messaging.RabbitMq;

/// <summary>
/// Publishes persistent messages with publisher confirms and <c>mandatory</c>: <see cref="PublishAsync"/> completes
/// only after the broker stored the message in a queue, and throws if it was rejected or could not be routed.
/// The outbox marks a message as published only after this call returned.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnection connection, IOptions<RabbitMqOptions> options)
    : IMessagePublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1); // a channel must not be used concurrently
    private IChannel? _channel;

    public async Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { [RabbitMqHeaders.StreamKey] = message.StreamKey },
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                // Closed by a channel-level error (it will not recover) or never opened: open a fresh one.
                // During an outage CreateChannelAsync throws BrokerUnavailableException and the outbox retries later.
                if (_channel is not null)
                {
                    _channel.Dispose();
                    _channel = null;
                }

                _channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    cancellationToken);
                await RabbitMqTopology.DeclareExchangeAsync(_channel, _options, cancellationToken);
            }

            await _channel.BasicPublishAsync(
                _options.ExchangeName,
                routingKey: message.Type,
                mandatory: true,
                properties,
                Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        _lock.Dispose();
    }
}
