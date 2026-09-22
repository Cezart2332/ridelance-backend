using Application.Abstractions.Data;
using Application.FiscalProfiles;
using Domain.Expenses;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Domain.Taxes;
using Microsoft.EntityFrameworkCore;

namespace Application.FiscalEstimates;

public interface IFinancialSnapshotProvider
{
    Task<FinancialSnapshot> GetAsync(PfaRegistration pfa, PfaTaxProfile profile, DateOnly asOf, CancellationToken cancellationToken);
}

/// <summary>
/// Snapshotul financiar al anului, din sursa unică de venit a PFA-ului: evidența lunară
/// (<c>PfaMonthlyIncome</c>), cu toate lunile, procesate de contabil sau nu. Cheltuielile sunt cele
/// deductibile confirmate — aceleași pe care le folosește și dashboardul.
/// </summary>
internal sealed class FinancialSnapshotProvider(IApplicationDbContext context) : IFinancialSnapshotProvider
{
    private static readonly TaxObligationType[] AnnualTaxTypes =
        [TaxObligationType.Cas, TaxObligationType.Cass, TaxObligationType.ImpozitVenit];

    public async Task<FinancialSnapshot> GetAsync(
        PfaRegistration pfa,
        PfaTaxProfile profile,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pfa);
        ArgumentNullException.ThrowIfNull(profile);

        int year = profile.TaxYear;
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        DateOnly effectiveAsOf = asOf > yearEnd ? yearEnd : asOf;

        List<PfaMonthlyIncome> incomes = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == year)
            .ToListAsync(cancellationToken);

        var expenses = await context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfa.Id && e.Year == year && e.Status == ExpenseStatus.Confirmed)
            .GroupBy(e => e.Month)
            .Select(g => new { Month = g.Key, Amount = g.Sum(e => e.AmountRon ?? 0m) })
            .ToListAsync(cancellationToken);

        var months = Enumerable.Range(1, 12)
            .Select(month => new MonthFigures(
                month,
                // O singură înregistrare pe lună: venitul din platforme (net de comision) sau, dacă
                // contabilul a trecut altfel împărțirea cash/card, totalul acela — niciodată amândouă.
                incomes.Where(i => i.Month == month).Sum(i => i.ComputeVenitTotal()),
                expenses.Where(e => e.Month == month).Sum(e => e.Amount)))
            .ToList();

        decimal recordedPayments = await context.TaxObligations
            .AsNoTracking()
            .Where(o => o.PfaRegistrationId == pfa.Id
                && o.PeriodYear == year
                && o.Status == TaxObligationStatus.Platita
                && AnnualTaxTypes.Contains(o.Type))
            .SumAsync(o => o.AmountDue, cancellationToken);

        DateTime? accessUtc = profile.AccessGrantedAtUtc ?? pfa.OnboardingCompletedAtUtc;
        DateOnly? accessOn = accessUtc is DateTime at ? DateOnly.FromDateTime(FiscalProfileService.ToRomania(at)) : null;

        // De când trebuie acoperit anul: 1 ianuarie sau înființarea, dacă e mai târziu. Un PFA
        // înființat prin noi, fără dată cunoscută, n-are activitate înainte de acces.
        DateOnly requiredFrom = yearStart;
        if (profile.PfaRegisteredOn is DateOnly registered && registered > yearStart)
        {
            requiredFrom = registered;
        }
        else if (profile.PfaRegisteredOn is null && pfa.PfaSource != PfaSource.Existing && accessOn is DateOnly a && a > yearStart)
        {
            requiredFrom = a;
        }

        // De când avem date: accesul în RIDElance sau prima lună cu încasări, oricare e mai devreme.
        int? firstMonthWithData = months.Where(m => m.Income > 0).Select(m => (int?)m.Month).FirstOrDefault();
        DateOnly? coveredFrom = (accessOn, firstMonthWithData) switch
        {
            (DateOnly acc, int m) => Min(acc, new DateOnly(year, m, 1)),
            (DateOnly acc, null) => acc,
            (null, int m) => new DateOnly(year, m, 1),
            _ => null,
        };
        if (coveredFrom < yearStart)
        {
            coveredFrom = yearStart;
        }

        return new FinancialSnapshot(Guid.NewGuid(), effectiveAsOf, year, requiredFrom, coveredFrom, months, recordedPayments);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
