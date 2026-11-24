using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Messaging.RabbitMq;

/// <summary>
/// Consumes the queue with manual acknowledgements: ack only after the handler succeeded; on failure the
/// message is rejected without requeue and goes to the dead-letter queue, so a poison message cannot block the queue.
/// If the broker is down at startup it keeps trying; after that, the client's automatic recovery restores the consumer.
/// </summary>
internal sealed partial class RabbitMqConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                break;
            }
            catch (BrokerUnavailableException ex)
            {
                LogWaitingForBroker(ex.Message, _options.ReconnectDelay);
                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        var channel = await connection.CreateChannelAsync(null, stoppingToken);
        await RabbitMqTopology.DeclareQueueAsync(channel, _options, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, _options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, stoppingToken);
        _channel = channel;
        LogListening(_options.QueueName);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var properties = delivery.BasicProperties;
        if (!Guid.TryParse(properties.MessageId, out var messageId) || string.IsNullOrEmpty(properties.Type))
        {
            LogInvalidMessage(properties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        var message = new MessageEnvelope(
            messageId,
            properties.Type,
            RabbitMqHeaders.ReadString(properties.Headers, RabbitMqHeaders.StreamKey),
            Encoding.UTF8.GetString(delivery.Body.Span),
            DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime));

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageHandler>().HandleAsync(message, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
#pragma warning disable CA1031 // any handler failure must dead-letter the message instead of crashing the consumer
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogHandlerFailed(ex, message.Type, messageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            _channel.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on queue {Queue}")]
    private partial void LogListening(string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Reason} Trying again in {Delay}")]
    private partial void LogWaitingForBroker(string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message without a valid id or type ({MessageId}); sent to the DLQ")]
    private partial void LogInvalidMessage(string? messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed for {Type} (message {MessageId}); sent to the DLQ")]
    private partial void LogHandlerFailed(Exception ex, string type, Guid messageId);
}
