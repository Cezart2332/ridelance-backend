using Application.Abstractions.Data;
using Application.Uber;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.PfaRegistrations.MonthlyIncome;

/// <summary>
/// Rebuilds one month of income for a PFA from the platform data we hold:
/// Bolt orders synced from the API and Uber CSV imports.
/// <para>
/// Cash and card are not separate income — they are how the platform money was collected.
/// Bolt tells us per order (payment methods look like "cash-cash", "card-card", "business-card");
/// Uber reports the cash collected from riders, so the rest of the payout is card.
/// </para>
/// </summary>
public static class PfaMonthlyIncomeRecalculator
{
    private sealed record BoltOrderEarning(string PaymentMethod, decimal NetEarnings);

    /// <summary>
    /// Recalculates Bolt, Uber, cash, card and the estimated tax for one month.
    /// The caller is responsible for saving.
    /// </summary>
    public static async Task RecalculateAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        Guid userId,
        int year,
        int month,
        Guid updatedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        (DateTime startUtc, DateTime endUtc) = RomanianMonthRangeUtc(year, month);

        List<BoltOrderEarning> boltOrders = await context.BoltOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId
                && o.OrderStatus == "finished"
                && o.OrderCreatedTime >= startUtc
                && o.OrderCreatedTime < endUtc)
            .Select(o => new BoltOrderEarning(o.PaymentMethod, o.NetEarnings))
            .ToListAsync(cancellationToken);

        decimal boltCash = boltOrders.Where(IsCash).Sum(o => o.NetEarnings);
        decimal boltTotal = boltOrders.Sum(o => o.NetEarnings);
        decimal boltCard = boltTotal - boltCash;

        List<UberEarningsTotals> uberEarnings = await context.UberCsvImports
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfaRegistrationId
                && i.Year == year
                && i.Month == month
                && i.FileType == UberCsvParser.Earnings)
            .Select(i => new UberEarningsTotals(i.NetEarnings, i.CashCollected))
            .ToListAsync(cancellationToken);

        decimal uberTotal = uberEarnings.Sum(i => i.NetEarnings);
        decimal uberCash = uberEarnings.Sum(i => i.CashCollected);
        // Uber pays the card share into the account and lets the driver keep the cash,
        // so whatever is not cash was collected by card.
        decimal uberCard = Math.Max(0, uberTotal - uberCash);

        PfaMonthlyIncome income = await GetOrCreateAsync(context, pfaRegistrationId, year, month, cancellationToken);

        income.VenitBolt = boltTotal;
        income.VenitUber = uberTotal;

        // Only overwrite the split when we actually have platform data for the month;
        // otherwise the values a contabil typed in by hand would be wiped by a sync.
        if (boltOrders.Count > 0 || uberEarnings.Count > 0)
        {
            income.VenitCash = boltCash + uberCash;
            income.VenitCard = boltCard + uberCard;
        }

        income.UpdatedAtUtc = DateTime.UtcNow;
        income.UpdatedByUserId = updatedByUserId;

        await RecalculateTaxAsync(context, income, pfaRegistrationId, year, month, cancellationToken);
    }

    /// <summary>
    /// Recomputes the year-to-date tax estimate stored on a month, after its income changed.
    /// </summary>
    public static async Task RecalculateTaxAsync(
        IApplicationDbContext context,
        PfaMonthlyIncome income,
        Guid pfaRegistrationId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(income);

        decimal ytdGrossIncome = await context.PfaMonthlyIncomes
            .Where(i => i.PfaRegistrationId == pfaRegistrationId && i.Year == year && i.Month != month)
            .SumAsync(i => i.VenitBolt + i.VenitUber, cancellationToken)
            + income.ComputePlatformIncome();

        decimal ytdExpenses = await context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaRegistrationId && e.Year == year)
            .Join(
                context.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.Verified),
                e => e.DocumentId,
                d => d.Id,
                (e, _) => e.AmountRon ?? 0m)
            .SumAsync(cancellationToken);

        PfaTaxCalculator.TaxResult tax = PfaTaxCalculator.Compute(ytdGrossIncome, ytdExpenses, year);
        income.TaxeEstimate = tax.TotalTax;
    }

    /// <summary>Bolt payment methods are dash-separated, e.g. "cash-cash", "card-card", "business-card".</summary>
    private static bool IsCash(BoltOrderEarning order) =>
        order.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase);

    private static async Task<PfaMonthlyIncome> GetOrCreateAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
            .SingleOrDefaultAsync(
                i => i.PfaRegistrationId == pfaRegistrationId && i.Year == year && i.Month == month,
                cancellationToken);

        if (income is not null)
        {
            return income;
        }

        income = new PfaMonthlyIncome
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaRegistrationId,
            Year = year,
            Month = month,
        };

        context.PfaMonthlyIncomes.Add(income);
        return income;
    }

    /// <summary>A calendar month in Romania, expressed as a UTC half-open range.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) RomanianMonthRangeUtc(int year, int month)
    {
        TimeZoneInfo romania = GetRomaniaTimeZone();
        DateTime startLocal = new(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddMonths(1);

        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, romania),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, romania));
    }

    public static TimeZoneInfo GetRomaniaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
    }

    private sealed record UberEarningsTotals(decimal NetEarnings, decimal CashCollected);
}
