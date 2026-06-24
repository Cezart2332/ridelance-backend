using Application.Abstractions.Data;
using Application.PfaRegistrations;
using Domain.Bolt;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.Bolt;

public static class BoltMonthlyIncomeUpdater
{
    public static async Task UpdateAsync(
        IApplicationDbContext context,
        Guid userId,
        IEnumerable<DateTime> orderDatesUtc,
        CancellationToken ct)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pfa is null)
        {
            return;
        }

        TimeZoneInfo romania = GetRomaniaTimeZone();
        var periods = orderDatesUtc
            .DefaultIfEmpty(DateTime.UtcNow)
            .Select(d => TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(d), romania))
            .Select(d => new { d.Year, d.Month })
            .Distinct()
            .ToList();

        foreach (var period in periods)
        {
            DateTime startLocal = new(period.Year, period.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            DateTime endLocal = startLocal.AddMonths(1);
            DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, romania);
            DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, romania);

            decimal boltIncome = await context.BoltOrders
                .AsNoTracking()
                .Where(o => o.UserId == userId
                    && o.OrderStatus == "finished"
                    && o.OrderCreatedTime >= startUtc
                    && o.OrderCreatedTime < endUtc)
                .SumAsync(o => o.NetEarnings, ct);

            PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
                .SingleOrDefaultAsync(i => i.PfaRegistrationId == pfa.Id && i.Year == period.Year && i.Month == period.Month, ct);

            if (income is null)
            {
                income = new PfaMonthlyIncome
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfa.Id,
                    Year = period.Year,
                    Month = period.Month
                };
                context.PfaMonthlyIncomes.Add(income);
            }

            income.VenitBolt = boltIncome;
            income.UpdatedAtUtc = DateTime.UtcNow;
            income.UpdatedByUserId = userId;

            decimal ytdGrossIncome = await context.PfaMonthlyIncomes
                .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == period.Year && i.Month != period.Month)
                .SumAsync(i => i.VenitBolt + i.VenitUber, ct)
                + income.ComputePlatformIncome();

            decimal ytdExpenses = await context.DeductibleExpenses
                .AsNoTracking()
                .Where(e => e.PfaRegistrationId == pfa.Id && e.Year == period.Year)
                .Join(
                    context.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.Verified),
                    e => e.DocumentId,
                    d => d.Id,
                    (e, _) => e.AmountRon ?? 0m)
                .SumAsync(ct);

            PfaTaxCalculator.TaxResult tax = PfaTaxCalculator.Compute(ytdGrossIncome, ytdExpenses, period.Year);
            income.TaxeEstimate = tax.TotalTax;
        }
    }

    private static DateTime NormalizeUtc(DateTime dateTime) =>
        dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };

    private static TimeZoneInfo GetRomaniaTimeZone()
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
}
