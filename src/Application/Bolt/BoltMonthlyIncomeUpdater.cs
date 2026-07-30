using Application.Abstractions.Data;
using Application.PfaRegistrations.MonthlyIncome;
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
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(orderDatesUtc);

        PfaRegistration? pfa = await context.PfaRegistrations
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pfa is null)
        {
            return;
        }

        TimeZoneInfo romania = PfaMonthlyIncomeRecalculator.GetRomaniaTimeZone();
        var periods = orderDatesUtc
            .DefaultIfEmpty(DateTime.UtcNow)
            .Select(d => TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(d), romania))
            .Select(d => new { d.Year, d.Month })
            .Distinct()
            .ToList();

        foreach (var period in periods)
        {
            await PfaMonthlyIncomeRecalculator.RecalculateAsync(
                context,
                pfa.Id,
                userId,
                period.Year,
                period.Month,
                userId,
                ct);
        }
    }

    private static DateTime NormalizeUtc(DateTime dateTime) =>
        dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };
}
