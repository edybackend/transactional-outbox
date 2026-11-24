using Subscriptions;

namespace TransactionalOutbox.Api.Endpoints;

internal static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var subscriptions = app.MapGroup("/api/subscriptions").WithTags("Subscriptions");

        subscriptions.MapPost("/", async (StartSubscription request, SubscriptionService service, CancellationToken ct) =>
        {
            var result = await service.StartAsync(request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/subscriptions/{result.Value!.Id}", result.Value)
                : FailureResults.ToProblem(result.Failure!);
        })
            .WithSummary("Start a subscription (writes the row and a subscription.created.v1 outbox message in one transaction)");

        subscriptions.MapPost("/{id:guid}/cancel", async (Guid id, SubscriptionService service, CancellationToken ct) =>
            FailureResults.ToHttp(await service.CancelAsync(id, ct)))
            .WithSummary("Cancel a subscription (writes a subscription.cancelled.v1 outbox message)");

        subscriptions.MapGet("/{id:guid}", async (Guid id, SubscriptionService service, CancellationToken ct) =>
            FailureResults.ToHttp(await service.GetAsync(id, ct)));

        subscriptions.MapGet("/", async (SubscriptionService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)));

        return app;
    }
}
