using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Outbox;

/// <summary>
/// Background loop that keeps the outbox drained. A full batch is followed immediately by the next one;
/// otherwise the relay sleeps for <see cref="OutboxOptions.PollingInterval"/>.
/// </summary>
internal sealed partial class OutboxRelay<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelay<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchSize = options.Value.BatchSize;
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher<TContext>>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // e.g. the database is locked for a moment: log and try again on the next tick
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPassFailed(ex);
            }

            if (processed < batchSize)
            {
                await Task.Delay(options.Value.PollingInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay pass failed")]
    private partial void LogPassFailed(Exception ex);
}
