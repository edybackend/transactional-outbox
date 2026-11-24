using Messaging;
using Messaging.InMemory;

namespace TransactionalOutbox.Api.Endpoints;

internal sealed record BrokerState(string Transport, bool Available);

internal sealed record SetBrokerAvailability(bool Available);

internal static class BrokerEndpoints
{
    public static IEndpointRouteBuilder MapBrokerEndpoints(this IEndpointRouteBuilder app)
    {
        var broker = app.MapGroup("/api/broker").WithTags("Broker");

        broker.MapGet("/", (IBrokerStatus status) => TypedResults.Ok(new BrokerState(status.Transport, status.IsAvailable)))
            .WithSummary("Which transport is in use and whether it is reachable");

        broker.MapPut("/", (SetBrokerAvailability request, IServiceProvider services) =>
        {
            if (services.GetService<InMemoryBroker>() is not { } inMemory)
            {
                return Results.Problem(
                    "Outages can only be simulated with the in-memory transport. With RabbitMQ, run `docker compose stop rabbitmq`.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            inMemory.SetAvailable(request.Available);
            return Results.Ok(new BrokerState(inMemory.Transport, inMemory.IsAvailable));
        })
            .WithSummary("Simulate a broker outage (in-memory transport only)");

        return app;
    }
}
