using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Messaging.RabbitMq;

/// <summary>
/// The single AMQP connection of the process, opened on first use. It never blocks startup: if the broker is
/// down, callers get <see cref="BrokerUnavailableException"/> and try again later (the outbox relay does that).
/// Once connected, the client's automatic recovery reconnects the connection, channels and consumers by itself.
/// </summary>
public sealed partial class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnection> logger) : IBrokerStatus, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public string Transport => "RabbitMq";

    public bool IsAvailable => _connection is { IsOpen: true };

    public async Task<IChannel> CreateChannelAsync(CreateChannelOptions? channelOptions, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(channelOptions, cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(options.Value.ConnectionString),
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                };
                try
                {
                    _connection = await factory.CreateConnectionAsync(cancellationToken);
                    LogConnected(factory.Endpoint);
                }
                catch (BrokerUnreachableException ex)
                {
                    throw new BrokerUnavailableException($"RabbitMQ is not reachable at {factory.Endpoint}.", ex);
                }
            }

            // A connection that dropped is being recovered by the client; opening a second one would leak it.
            return _connection.IsOpen
                ? _connection
                : throw new BrokerUnavailableException("The RabbitMQ connection is down and recovering.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _lock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Endpoint}")]
    private partial void LogConnected(AmqpTcpEndpoint endpoint);
}
