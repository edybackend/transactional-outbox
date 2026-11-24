namespace Messaging;

/// <summary>Sends a message to the broker. Completes only after the broker accepted it; throws otherwise.</summary>
public interface IMessagePublisher
{
    Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken);
}

/// <summary>Processes one delivered message. Throwing means "not processed": the transport will not acknowledge it.</summary>
public interface IMessageHandler
{
    Task HandleAsync(MessageEnvelope message, CancellationToken cancellationToken);
}

/// <summary>What the API reports about the transport (<c>GET /api/broker</c>).</summary>
public interface IBrokerStatus
{
    string Transport { get; }

    bool IsAvailable { get; }
}

/// <summary>The broker cannot be reached right now. The outbox keeps the message and tries again later.</summary>
public sealed class BrokerUnavailableException : Exception
{
    public BrokerUnavailableException()
        : this("The message broker is unavailable.")
    {
    }

    public BrokerUnavailableException(string message)
        : base(message)
    {
    }

    public BrokerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
