using System.Text.Json.Serialization;
using Billing;
using Messaging;
using Messaging.RabbitMq;
using Scalar.AspNetCore;
using Subscriptions;
using TransactionalOutbox.Api;
using TransactionalOutbox.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
if (string.Equals(builder.Configuration["Messaging:Transport"], "RabbitMq", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddRabbitMqTransport(builder.Configuration);
}
else
{
    builder.Services.AddInMemoryTransport();
}

builder.Services.AddSubscriptionsModule(builder.Configuration);
builder.Services.AddBillingModule(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

await app.Services.InitializeSubscriptionsDatabaseAsync();
await app.Services.InitializeBillingDatabaseAsync();
if (app.Configuration.GetValue("Samples:Seed", false))
{
    await SampleData.SeedAsync(app.Services, CancellationToken.None);
}

// Malformed JSON raises BadHttpRequestException, which carries its own 400; without the selector it becomes a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapSubscriptionEndpoints();
app.MapOutboxEndpoints();
app.MapBillingEndpoints();
app.MapBrokerEndpoints();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
