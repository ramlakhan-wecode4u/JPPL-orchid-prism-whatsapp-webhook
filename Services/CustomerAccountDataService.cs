using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

/// <summary>
/// Read-only queries over the main Orchid Prism order and billing tables.
/// Every query is scoped to the customer resolved from the sender's verified WhatsApp number.
/// </summary>
public sealed class CustomerAccountDataService(AppDbContext db)
{
    public const int MaxRecentOrders = 10;

    private const string DeclinedOrderStatusCode = "DECLINED";

    // Bills in these states have no amount the customer still owes.
    private static readonly string[] ClosedBillStatuses = ["Draft", "Paid", "Cancelled"];

    public async Task<IReadOnlyList<OrderSummary>> GetRecentOrdersAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return await (
                from order in db.Orders.AsNoTracking()
                join status in db.Statuses.AsNoTracking() on order.StatusId equals status.Id
                where order.CustomerId == customerId &&
                      status.Code != DeclinedOrderStatusCode
                orderby order.CreatedAt descending, order.Id descending
                select new OrderSummary(
                    order.Id,
                    order.CreatedAt,
                    status.Name,
                    db.EstimateVersions
                        .Where(version => version.Id == (order.ApprovedEstimateVersionId ?? order.CurrentEstimateVersionId))
                        .Select(version => (decimal?)version.TotalPrice)
                        .FirstOrDefault()))
            .Take(MaxRecentOrders)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderDetails?> GetOrderDetailsAsync(
        int customerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        // Filtering by CustomerId stops one customer from opening another customer's order by ID.
        var order = await (
                from item in db.Orders.AsNoTracking()
                join status in db.Statuses.AsNoTracking() on item.StatusId equals status.Id
                where item.Id == orderId && item.CustomerId == customerId
                select new
                {
                    item.Id,
                    item.CreatedAt,
                    StatusName = status.Name,
                    Amount = db.EstimateVersions
                        .Where(version => version.Id == (item.ApprovedEstimateVersionId ?? item.CurrentEstimateVersionId))
                        .Select(version => (decimal?)version.TotalPrice)
                        .FirstOrDefault()
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var bills = await db.CustomerBills.AsNoTracking()
            .Where(bill => bill.OrderId == orderId &&
                           bill.CustomerId == customerId &&
                           bill.Status != "Cancelled")
            .OrderBy(bill => bill.BillDate)
            .Select(bill => new BillSummary(
                bill.BillNumber,
                bill.BillDate,
                bill.TotalAmount,
                bill.PaidAmount,
                bill.OutstandingAmount,
                bill.CreditDueDate,
                bill.Status))
            .ToListAsync(cancellationToken);

        return new OrderDetails(order.Id, order.CreatedAt, order.StatusName, order.Amount, bills);
    }

    public async Task<BalanceSummary> GetBalanceAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var advanceBalance = await db.CustomerBalances.AsNoTracking()
            .Where(balance => balance.CustomerId == customerId)
            .SumAsync(balance => (decimal?)balance.BalanceAmount, cancellationToken) ?? 0m;

        var outstanding = await GetOutstandingAsync(customerId, cancellationToken);
        return new BalanceSummary(advanceBalance, outstanding.Total, outstanding.Overdue);
    }

    public async Task<CreditSummary?> GetCreditAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var paymentTerm = await db.CustomerPaymentTerms.AsNoTracking()
            .Where(term => term.CustomerId == customerId && term.IsActive)
            .OrderByDescending(term => term.CreatedAt)
            .ThenByDescending(term => term.Id)
            .Select(term => new { term.CreditLimit, term.CreditDays })
            .FirstOrDefaultAsync(cancellationToken);

        if (paymentTerm is null)
        {
            return null;
        }

        var outstanding = await GetOutstandingAsync(customerId, cancellationToken);
        var available = Math.Max(0m, paymentTerm.CreditLimit - outstanding.Total);

        return new CreditSummary(
            paymentTerm.CreditLimit,
            paymentTerm.CreditDays,
            outstanding.Total,
            available,
            outstanding.Overdue);
    }

    private async Task<(decimal Total, decimal Overdue)> GetOutstandingAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var today = IndiaClock.Today;

        var openBills = db.CustomerBills.AsNoTracking()
            .Where(bill => bill.CustomerId == customerId &&
                           bill.OutstandingAmount > 0 &&
                           !ClosedBillStatuses.Contains(bill.Status));

        var total = await openBills
            .SumAsync(bill => (decimal?)bill.OutstandingAmount, cancellationToken) ?? 0m;

        var overdue = await openBills
            .Where(bill => bill.Status == "Overdue" ||
                           (bill.CreditDueDate != null && bill.CreditDueDate < today))
            .SumAsync(bill => (decimal?)bill.OutstandingAmount, cancellationToken) ?? 0m;

        return (total, overdue);
    }
}

public sealed record OrderSummary(int Id, DateTime CreatedAt, string StatusName, decimal? Amount);

public sealed record OrderDetails(
    int Id,
    DateTime CreatedAt,
    string StatusName,
    decimal? Amount,
    IReadOnlyList<BillSummary> Bills);

public sealed record BillSummary(
    string BillNumber,
    DateTime BillDate,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    DateOnly? CreditDueDate,
    string Status);

public sealed record BalanceSummary(decimal AdvanceBalance, decimal Outstanding, decimal Overdue);

public sealed record CreditSummary(
    decimal CreditLimit,
    int CreditDays,
    decimal UsedCredit,
    decimal AvailableCredit,
    decimal Overdue);

public static class IndiaClock
{
    // India has no daylight saving, so a fixed +05:30 offset is exact and works on every host OS.
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow + IndiaOffset);
}
