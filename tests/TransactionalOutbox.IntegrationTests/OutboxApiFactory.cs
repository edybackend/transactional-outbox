using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TransactionalOutbox.IntegrationTests;

/// <summary>The real API with both modules, the relay and the consumer, on throw-away SQLite files and fast timings.</summary>
public class OutboxApiFactory : WebApplicationFactory<Program>
{
    private readonly TempDatabase _subscriptions = new();
    private readonly TempDatabase _billing = new();

    protected virtual void Configure(IWebHostBuilder builder)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Subscriptions", _subscriptions.ConnectionString);
        builder.UseSetting("ConnectionStrings:Billing", _billing.ConnectionString);
        builder.UseSetting("Samples:Seed", "false");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.050");
        builder.UseSetting("Outbox:BaseBackoff", "00:00:00.050");
        builder.UseSetting("Outbox:MaxBackoff", "00:00:00.200");
        Configure(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _subscriptions.Dispose();
        _billing.Dispose();
    }
}

/// <summary>The RabbitMQ transport pointed at a port where nothing listens: a broker that is down from the start.</summary>
public sealed class UnreachableRabbitMqFactory : OutboxApiFactory
{
    protected override void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Messaging:Transport", "RabbitMq");
        builder.UseSetting("RabbitMq:ConnectionString", "amqp://guest:guest@127.0.0.1:1");
    }
}

/// <summary>A real RabbitMQ from <c>RABBITMQ_URL</c> (CI starts one), with names unique to this run.</summary>
public sealed class RealRabbitMqFactory : OutboxApiFactory
{
    public static string? Url => Environment.GetEnvironmentVariable("RABBITMQ_URL");

    protected override void Configure(IWebHostBuilder builder)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        builder.UseSetting("Messaging:Transport", "RabbitMq");
        builder.UseSetting("RabbitMq:ConnectionString", Url ?? string.Empty);
        builder.UseSetting("RabbitMq:ExchangeName", $"test-{suffix}");
        builder.UseSetting("RabbitMq:QueueName", $"test-{suffix}.billing");
    }
}
