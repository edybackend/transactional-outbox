using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Messaging.InMemory;

/// <summary>
/// Delivers in-memory messages to the registered <see cref="IMessageHandler"/>, one at a time, in publish order.
/// A message whose handler throws is dropped and logged, like a message sent to a dead-letter queue.
/// </summary>
internal sealed partial class InMemoryConsumer(
    InMemoryBroker broker,
    IServiceScopeFactory scopeFactory,
    ILogger<InMemoryConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in broker.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMessageHandler>().HandleAsync(message, stoppingToken);
                }
#pragma warning disable CA1031 // a failing handler must not stop the consumer
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    LogHandlerFailed(ex, message.Type, message.MessageId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed for {Type} (message {MessageId}); message dead-lettered")]
    private partial void LogHandlerFailed(Exception ex, string type, Guid messageId);
}
