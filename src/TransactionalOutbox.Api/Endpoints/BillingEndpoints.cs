using Billing;

namespace TransactionalOutbox.Api.Endpoints;

internal static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/invoices", async (Guid? subscriptionId, BillingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.ListInvoicesAsync(subscriptionId, ct)))
            .WithTags("Billing")
            .WithSummary("Invoices issued by the Billing consumer, newest first");
        return app;
    }
}
