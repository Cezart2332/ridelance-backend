using System.Globalization;

namespace Application.FiscalEstimates;

/// <summary>Venitul și cheltuielile unei luni din anul fiscal, din evidența PFA-ului.</summary>
public sealed record MonthFigures(int Month, decimal Income, decimal Expenses);

/// <summary>
/// Snapshotul financiar pe care rulează motorul (spec §4.1). Venitul vine din evidența lunară a
/// PFA-ului (<c>PfaMonthlyIncome</c>: Bolt din API și Uber din CSV, net de comision, deci comisionul
/// nu mai apare și ca cheltuială); cheltuielile, din cele deductibile cu document verificat.
/// Tranzacțiile bancare nu intră ca venit.
/// </summary>
/// <param name="RequiredFrom">De când trebuie să avem date: 1 ianuarie sau data înființării, dacă e mai târziu.</param>
/// <param name="CoveredFrom">De când avem date în RIDElance; <c>null</c> = deloc.</param>
public sealed record FinancialSnapshot(
    Guid Id,
    DateOnly AsOf,
    int TaxYear,
    DateOnly RequiredFrom,
    DateOnly? CoveredFrom,
    IReadOnlyList<MonthFigures> Months,
    decimal RecordedTaxPayments)
{
    public decimal GrossIncomeYtd => Months.Where(m => m.Month <= AsOf.Month).Sum(m => m.Income);
    public decimal DeductibleExpensesYtd => Months.Where(m => m.Month <= AsOf.Month).Sum(m => m.Expenses);
}

/// <summary>
/// Netul anual estimat (spec §4.3), fără anualizare ×12 și fără proratarea plafoanelor.
/// </summary>
/// <remarks>
/// Evidența e lunară (Uber livrează doar totaluri pe lună), deci săptămânile reprezentative sunt
/// zilele din ultimele luni complete, cu activitate, până la 8 săptămâni: media săptămânală =
/// netul lor ÷ zile × 7. Sub 4 săptămâni (28 de zile) nu estimăm.
/// </remarks>
public static class IncomeProjector
{
    public const int MinRepresentativeDays = 28;
    public const int MaxRepresentativeDays = 56;

    public static IncomeProjection Project(FinancialSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var yearEnd = new DateOnly(snapshot.TaxYear, 12, 31);
        decimal weeksRemaining = Math.Max(0, yearEnd.DayNumber - snapshot.AsOf.DayNumber) / 7m;
        decimal netRealized = snapshot.GrossIncomeYtd - snapshot.DeductibleExpensesYtd;

        if (snapshot.CoveredFrom is not DateOnly coveredFrom)
        {
            return Unavailable(TaxReasons.CoverageGap, [Interval(snapshot.RequiredFrom, snapshot.AsOf)], netRealized, weeksRemaining);
        }

        // Lipsa datelor nu înseamnă venit zero: o perioadă neacoperită oprește estimarea.
        if (coveredFrom > snapshot.RequiredFrom)
        {
            return Unavailable(TaxReasons.CoverageGap, [Interval(snapshot.RequiredFrom, coveredFrom.AddDays(-1))], netRealized, weeksRemaining);
        }

        decimal representativeNet = 0;
        int representativeDays = 0;
        for (int month = snapshot.AsOf.Month - 1; month >= 1 && representativeDays < MaxRepresentativeDays; month--)
        {
            var monthStart = new DateOnly(snapshot.TaxYear, month, 1);
            DateOnly monthEnd = monthStart.AddMonths(1).AddDays(-1);
            DateOnly from = monthStart > coveredFrom ? monthStart : coveredFrom;
            if (from > monthEnd)
            {
                break;
            }

            MonthFigures? figures = snapshot.Months.FirstOrDefault(m => m.Month == month);
            // O lună fără nicio încasare nu e o lună de activitate (concediu, pauză).
            if (figures is null || figures.Income <= 0)
            {
                continue;
            }

            representativeNet += figures.Income - figures.Expenses;
            representativeDays += monthEnd.DayNumber - from.DayNumber + 1;
        }

        if (representativeDays < MinRepresentativeDays)
        {
            return Unavailable(TaxReasons.ShortHistory, ["4 săptămâni de activitate"], netRealized, weeksRemaining);
        }

        decimal weeklyAverage = representativeNet / representativeDays * 7m;
        decimal netAnnual = netRealized + weeklyAverage * weeksRemaining;

        return new IncomeProjection(
            Math.Round(netAnnual, 2, MidpointRounding.AwayFromZero),
            null,
            [],
            Math.Round(netRealized, 2, MidpointRounding.AwayFromZero),
            Math.Round(weeklyAverage, 2, MidpointRounding.AwayFromZero),
            Math.Round(representativeDays / 7m, 1, MidpointRounding.AwayFromZero),
            Math.Round(weeksRemaining, 1, MidpointRounding.AwayFromZero));
    }

    private static IncomeProjection Unavailable(string reason, IReadOnlyList<string> missing, decimal netRealized, decimal weeksRemaining) =>
        new(null, reason, missing, Math.Round(netRealized, 2), null, 0, Math.Round(weeksRemaining, 1));

    private static string Interval(DateOnly from, DateOnly to) =>
        $"{from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} – {to.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
}
