using System.Threading.Channels;

namespace Messaging.InMemory;

/// <summary>
/// An in-process broker for <c>dotnet run</c> and the tests. <see cref="SetAvailable"/> simulates an outage:
/// while the broker is down, every publish fails, exactly like a stopped RabbitMQ.
/// </summary>
public sealed class InMemoryBroker : IMessagePublisher, IBrokerStatus
{
    private readonly Channel<MessageEnvelope> _queue = Channel.CreateUnbounded<MessageEnvelope>(
        new UnboundedChannelOptions { SingleReader = true });

    private volatile bool _available = true;

    public string Transport => "InMemory";

    public bool IsAvailable => _available;

    internal ChannelReader<MessageEnvelope> Reader => _queue.Reader;

    public void SetAvailable(bool available) => _available = available;

    public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_available)
        {
            throw new BrokerUnavailableException("The in-memory broker is down (simulated outage).");
        }

        return _queue.Writer.WriteAsync(message, cancellationToken).AsTask();
    }
}
