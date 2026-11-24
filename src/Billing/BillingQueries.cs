using Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Billing;

public sealed record InvoiceView(Guid Id, Guid SubscriptionId, string CustomerEmail, decimal Amount, InvoiceStatus Status, DateTimeOffset IssuedAt);

public sealed class BillingQueries(BillingDbContext db)
{
    /// <summary>Newest first.</summary>
    public async Task<IReadOnlyList<InvoiceView>> ListInvoicesAsync(Guid? subscriptionId, CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking();
        if (subscriptionId is not null)
        {
            query = query.Where(i => i.SubscriptionId == subscriptionId);
        }

        return await query
            .OrderByDescending(i => i.IssuedAt)
            .Take(100)
            .Select(i => new InvoiceView(i.Id, i.SubscriptionId, i.CustomerEmail, i.Amount, i.Status, i.IssuedAt))
            .ToListAsync(cancellationToken);
    }
}
