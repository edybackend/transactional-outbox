using Outbox;
using Subscriptions.Persistence;

namespace TransactionalOutbox.Api.Endpoints;

internal static class OutboxEndpoints
{
    public static IEndpointRouteBuilder MapOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        var outbox = app.MapGroup("/api/outbox").WithTags("Outbox");

        outbox.MapGet("/stats", async (OutboxAdmin<SubscriptionsDbContext> admin, CancellationToken ct) =>
            TypedResults.Ok(await admin.GetStatsAsync(ct)))
            .WithSummary("Count outbox messages per status");

        outbox.MapGet("/", async (OutboxStatus? status, int? take, OutboxAdmin<SubscriptionsDbContext> admin, CancellationToken ct) =>
            TypedResults.Ok(await admin.ListAsync(status, Math.Clamp(take ?? 50, 1, 200), ct)))
            .WithSummary("List outbox messages, newest first (attempts, next attempt, last error)");

        outbox.MapPost("/{messageId:guid}/requeue", async (Guid messageId, OutboxAdmin<SubscriptionsDbContext> admin, CancellationToken ct) =>
            await admin.RequeueAsync(messageId, ct) switch
            {
                RequeueOutcome.Requeued => Results.NoContent(),
                RequeueOutcome.NotFound => Results.Problem($"Outbox message {messageId} was not found.", statusCode: StatusCodes.Status404NotFound),
                _ => Results.Problem("Only dead-lettered messages can be requeued.", statusCode: StatusCodes.Status409Conflict),
            })
            .WithSummary("Put a dead-lettered message back in the queue");

        return app;
    }
}
