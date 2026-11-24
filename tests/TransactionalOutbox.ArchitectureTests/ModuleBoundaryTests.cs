using System.Reflection;
using NetArchTest.Rules;

namespace TransactionalOutbox.ArchitectureTests;

/// <summary>
/// The modules talk only through integration events. These rules keep it that way, and keep the outbox
/// the only way out of the Subscriptions module.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly SubscriptionsAssembly = typeof(Subscriptions.SubscriptionService).Assembly;
    private static readonly Assembly BillingAssembly = typeof(Billing.SubscriptionEventsHandler).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(Subscriptions.Contracts.SubscriptionEventTypes).Assembly;
    private static readonly Assembly OutboxAssembly = typeof(Outbox.OutboxMessage).Assembly;
    private static readonly Assembly MessagingAssembly = typeof(Messaging.IMessagePublisher).Assembly;

    [Fact]
    public void Subscriptions_never_publishes_directly_it_only_writes_to_the_outbox() =>
        AssertNoDependency(SubscriptionsAssembly, "Messaging", "Billing", "RabbitMQ", "Microsoft.AspNetCore");

    [Fact]
    public void Billing_knows_subscriptions_only_through_the_contracts() =>
        AssertNoDependency(BillingAssembly, "Subscriptions.Persistence", "Outbox", "RabbitMQ", "Microsoft.AspNetCore");

    [Fact]
    public void Billing_does_not_use_subscription_internals()
    {
        var result = Types.InAssembly(BillingAssembly)
            .ShouldNot().HaveDependencyOnAny("Subscriptions.SubscriptionService", "Subscriptions.Subscription", "Subscriptions.Plan")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Contracts_depend_on_nothing_of_ours() =>
        AssertNoDependency(ContractsAssembly, "Subscriptions.Persistence", "Billing", "Outbox", "Messaging", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void The_outbox_is_generic_it_knows_no_module_and_no_broker() =>
        AssertNoDependency(OutboxAssembly, "Subscriptions", "Billing", "RabbitMQ", "Messaging.RabbitMq", "Microsoft.AspNetCore");

    [Fact]
    public void Messaging_abstractions_know_no_database_and_no_broker_client() =>
        AssertNoDependency(MessagingAssembly, "Microsoft.EntityFrameworkCore", "RabbitMQ", "Outbox", "Subscriptions", "Billing");

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
