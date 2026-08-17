using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations;
using Domain.Bolt;
using Domain.Documents;
using Domain.Expenses;
using Domain.PfaRegistrations;
using Domain.Uber;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.PfaDashboard;

/* ── Contract de răspuns ──────────────────────────────────────────────────── */

public sealed record PfaDashboardPeriodResponse(DateOnly From, DateOnly To, string Granularity);

/// <summary><c>Previous</c> e null când nu există date în perioada de comparație — UI-ul nu afișează delta.</summary>
public sealed record PfaDashboardMetric(decimal Value, decimal? Previous);

public sealed record PfaPlatformAmounts(decimal Bolt, decimal Uber);

public sealed record PfaDashboardFeeMetric(decimal Value, decimal? Previous, PfaPlatformAmounts ByPlatform);

public sealed record PfaDashboardKpisResponse(
    PfaDashboardMetric NetEarnings,
    PfaDashboardFeeMetric PlatformFees,
    PfaDashboardMetric OnlineHours,
    PfaDashboardMetric RideKm,
    PfaDashboardMetric NetPerHour,
    PfaDashboardMetric NetPerKm);

public sealed record PfaTaxComponentResponse(
    string Key,
    string Label,
    decimal Amount,
    decimal? Rate,
    decimal? Basis,
    string? Note);

public sealed record PfaFiscalMonthReferenceResponse(string Month, decimal Total);

public sealed record PfaTaxReserveResponse(
    string Scope,
    decimal Total,
    List<PfaTaxComponentResponse> Components,
    PfaFiscalMonthReferenceResponse FiscalMonth);

/// <param name="ExpensesAwaitingReview">
/// Cât din cheltuielile perioadei a intrat în calcul, dar are documentul încă neverificat de
/// RIDElance. Zero înseamnă că totul e validat. Cifra există ca profitul să nu pară mai sigur
/// decât e: suma contează deja, dar nu a trecut încă pe la un om.
/// </param>
public sealed record PfaRealProfitResponse(
    decimal NetEarnings,
    decimal DeductibleExpenses,
    decimal EstimatedTaxes,
    decimal Value,
    decimal? RetentionRatio,
    decimal ExpensesAwaitingReview);

public sealed record PfaPlatformSplitResponse(
    string Platform,
    decimal Net,
    decimal Fees,
    decimal Cash,
    decimal Card,
    int Rides);

public sealed record PfaNetEarningsPointResponse(
    string Bucket,
    string Label,
    decimal Bolt,
    decimal Uber,
    decimal Total,
    int Rides);

public sealed record PfaFeesAndTaxesPointResponse(
    string Bucket,
    string Label,
    decimal BoltFee,
    decimal UberFee,
    decimal VatIntracom,
    decimal BoltNonResident);

/// <summary>
/// Un bucket din seria financiară. Cheltuielile și taxele nu au dată proprie, deci se
/// repartizează proporțional cu încasările — repartizarea se face aici, o singură dată, și
/// se trimite gata calculată. Frontendul nu are voie să o refacă: dacă regula se schimbă,
/// un grafic care și-o deduce singur ar diverge tăcut de linia de profit de lângă el.
/// </summary>
public sealed record PfaRealProfitPointResponse(
    string Bucket,
    string Label,
    decimal NetEarnings,
    decimal DeductibleExpenses,
    decimal EstimatedTaxes,
    decimal Value);

public sealed record PfaDashboardSeriesResponse(
    List<PfaNetEarningsPointResponse> NetEarnings,
    List<PfaFeesAndTaxesPointResponse> FeesAndTaxes,
    List<PfaRealProfitPointResponse> RealProfit);

/// <remarks>
/// <c>OnboardingPending</c>: contul de flotă a fost configurat în onboarding, dar platforma nu
/// l-a activat încă. Dashboardul arată atunci un card informativ, nu un CTA de conectare — cine
/// tocmai a completat pasul nu trebuie trimis să-l facă din nou (spec fix-uri §12).
/// </remarks>
public sealed record PfaBoltSourceResponse(
    bool Configured,
    bool Connected,
    DateTime? LastSyncAt,
    string? ErrorMessage,
    bool OnboardingPending = false);

public sealed record PfaUberSourceResponse(
    bool Connected,
    DateOnly? LastReportAt,
    string? DetectedRange,
    bool OnboardingPending = false);

public sealed record PfaDashboardSourcesResponse(PfaBoltSourceResponse Bolt, PfaUberSourceResponse Uber);

/// <remarks>
/// <c>UberIsMonthlyAggregate</c> semnalează UI-ului că Uber livrează doar totaluri lunare;
/// în serii ele apar împărțite proporțional pe bucket-uri, nu măsurate zilnic.
/// </remarks>
public sealed record PfaDashboardSummaryResponse(
    PfaDashboardPeriodResponse Period,
    PfaDashboardKpisResponse Kpis,
    PfaTaxReserveResponse TaxReserve,
    PfaRealProfitResponse RealProfit,
    List<PfaPlatformSplitResponse> PlatformSplit,
    PfaDashboardSeriesResponse Series,
    PfaDashboardSourcesResponse Sources,
    bool UberIsMonthlyAggregate);

/* ── Query ────────────────────────────────────────────────────────────────── */

public sealed record GetPfaDashboardSummaryQuery(
    DateOnly From,
    DateOnly To,
    string? Platform,
    string? Payment) : IQuery<PfaDashboardSummaryResponse>;

internal sealed class GetPfaDashboardSummaryQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IOptions<FiscalPolicyOptions> fiscalOptions)
    : IQueryHandler<GetPfaDashboardSummaryQuery, PfaDashboardSummaryResponse>
{
    private static readonly CultureInfo RomanianCulture = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Peste două luni, granularitatea zilnică devine ilizibilă — se trece pe luni.</summary>
    private const int MaxDaysForDailyBuckets = 62;

    private readonly FiscalPolicyOptions _fiscal = fiscalOptions.Value;

    public async Task<Result<PfaDashboardSummaryResponse>> Handle(
        GetPfaDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        if (query.To < query.From)
        {
            return Result.Failure<PfaDashboardSummaryResponse>(
                Error.Problem("PfaDashboard.InvalidRange", "Sfârșitul perioadei nu poate fi înaintea începutului."));
        }

        string platform = NormalizePlatform(query.Platform);
        string payment = NormalizePayment(query.Payment);
        var period = new PfaDashboardPeriod(query.From, query.To);
        Guid userId = userContext.UserId;
        TimeZoneInfo timeZone = PfaDashboardPeriod.RomaniaTimeZone();

        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        BoltIntegration? integration = await context.BoltIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(bi => bi.UserId == userId, cancellationToken);

        // Ce s-a configurat în onboarding pentru fiecare platformă. Fără asta, dashboardul unui
        // cont proaspăt înrolat îi cerea utilizatorului să conecteze exact ce tocmai completase.
        List<PfaPlatformAccount> platformAccounts = pfa is null
            ? []
            : await context.PfaPlatformAccounts
                .AsNoTracking()
                .Where(a => a.PfaRegistrationId == pfa.Id && a.IsSelectedByUser)
                .ToListAsync(cancellationToken);

        bool BoltOnboarded() => IsAwaitingActivation(platformAccounts, PfaPlatformProvider.Bolt);
        bool UberOnboarded() => IsAwaitingActivation(platformAccounts, PfaPlatformProvider.Uber);

        // Un singur tur la bază pentru ambele perioade: se citește intervalul lărgit,
        // apoi se filtrează în memorie pe perioada curentă și pe cea de comparație.
        PfaDashboardPeriod previousPeriod = period.Previous();
        (DateTime windowStartUtc, _) = previousPeriod.ToUtcBounds(timeZone);
        PfaDashboardPeriod fiscalMonth = period.FiscalMonth();
        DateOnly windowEnd = fiscalMonth.To > period.To ? fiscalMonth.To : period.To;
        (_, DateTime windowEndUtc) = new PfaDashboardPeriod(period.From, windowEnd).ToUtcBounds(timeZone);

        List<BoltOrder> boltOrders = platform == "uber"
            ? []
            : await context.BoltOrders
                .AsNoTracking()
                .Where(o => o.UserId == userId
                    && o.OrderStatus == "finished"
                    && o.OrderCreatedTime >= windowStartUtc
                    && o.OrderCreatedTime < windowEndUtc)
                .ToListAsync(cancellationToken);

        List<UberCsvImport> uberImports = platform == "bolt" || pfa is null
            ? []
            : await context.UberCsvImports
                .AsNoTracking()
                .Where(i => i.PfaRegistrationId == pfa.Id)
                .ToListAsync(cancellationToken);

        // Confirmarea utilizatorului decide ce intră în calcul: cheltuiala are efect imediat,
        // cum cere spec-ul §7.2. Verificarea documentului de către admin rămâne un semnal
        // separat — se numără mai jos și se afișează ca „în verificare", ca userul să vadă și
        // efectul, și faptul că suma nu e încă validată.
        var confirmedExpenses = pfa is null
            ? []
            : await context.DeductibleExpenses
                .AsNoTracking()
                .Where(e => e.PfaRegistrationId == pfa.Id && e.Status == ExpenseStatus.Confirmed)
                .Join(
                    context.Documents.AsNoTracking(),
                    e => e.DocumentId,
                    d => d.Id,
                    (e, d) => new { e.Year, e.Month, e.AmountRon, DocumentStatus = d.Status })
                .ToListAsync(cancellationToken);

        List<(int Year, int Month, decimal Amount)> verifiedExpenses = confirmedExpenses
            .Select(e => (e.Year, e.Month, e.AmountRon ?? 0m))
            .ToList();

        int fiscalYear = period.To.Year;
        List<PfaMonthlyIncome> yearIncomes = pfa is null
            ? []
            : await context.PfaMonthlyIncomes
                .AsNoTracking()
                .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == fiscalYear)
                .ToListAsync(cancellationToken);

        decimal annualIncome = yearIncomes.Sum(i => i.ComputePlatformIncome());
        decimal annualExpenses = verifiedExpenses.Where(e => e.Year == fiscalYear).Sum(e => e.Amount);
        PfaTaxCalculator.TaxResult annualTaxes = PfaTaxCalculator.Compute(annualIncome, annualExpenses, fiscalYear);

        // Aceleași luni ca intervalul afișat, dar doar partea cu documentul încă neverificat.
        List<(int Year, int Month, decimal Amount)> awaitingReview = confirmedExpenses
            .Where(e => e.DocumentStatus != DocumentStatus.Verified)
            .Select(e => (e.Year, e.Month, e.AmountRon ?? 0m))
            .ToList();

        var aggregation = new AggregationContext(boltOrders, uberImports, verifiedExpenses, timeZone, platform, payment);
        var awaitingAggregation = new AggregationContext(boltOrders, uberImports, awaitingReview, timeZone, platform, payment);

        PeriodTotals current = Aggregate(aggregation, period);
        PeriodTotals previous = Aggregate(aggregation, previousPeriod);
        PeriodTotals fiscalMonthTotals = period.IsWholeCalendarMonth()
            ? current
            : Aggregate(aggregation, fiscalMonth);

        TaxReserveResult currentReserve = BuildReserve(current, annualIncome, annualTaxes);
        TaxReserveResult fiscalMonthReserve = period.IsWholeCalendarMonth()
            ? currentReserve
            : BuildReserve(fiscalMonthTotals, annualIncome, annualTaxes);

        string granularity = period.DayCount <= MaxDaysForDailyBuckets ? "day" : "month";
        PfaDashboardSeriesResponse series = BuildSeries(aggregation, period, granularity, current, currentReserve);

        decimal realProfit = TaxReserveCalculator.RealProfit(
            current.Net,
            current.DeductibleExpenses,
            currentReserve.Total);

        return new PfaDashboardSummaryResponse(
            new PfaDashboardPeriodResponse(period.From, period.To, granularity),
            BuildKpis(current, previous),
            new PfaTaxReserveResponse(
                period.IsWholeCalendarMonth() ? "fiscalMonth" : "period",
                Round(currentReserve.Total),
                currentReserve.Components,
                new PfaFiscalMonthReferenceResponse(
                    $"{fiscalMonth.From.Year:D4}-{fiscalMonth.From.Month:D2}",
                    Round(fiscalMonthReserve.Total))),
            new PfaRealProfitResponse(
                Round(current.Net),
                Round(current.DeductibleExpenses),
                Round(currentReserve.Total),
                Round(realProfit),
                current.Net > 0 ? Math.Round(realProfit / current.Net, 4) : null,
                Round(Aggregate(awaitingAggregation, period).DeductibleExpenses)),
            BuildPlatformSplit(current, platform),
            series,
            new PfaDashboardSourcesResponse(
                new PfaBoltSourceResponse(
                    integration is not null,
                    integration?.IsConnected ?? false,
                    integration?.LastFetchedAtUtc,
                    integration?.ErrorMessage,
                    BoltOnboarded()),
                BuildUberSource(uberImports) with { OnboardingPending = UberOnboarded() }),
            uberImports.Count > 0);
    }

    /* ── Agregare ─────────────────────────────────────────────────────────── */

    private sealed record AggregationContext(
        List<BoltOrder> BoltOrders,
        List<UberCsvImport> UberImports,
        List<(int Year, int Month, decimal Amount)> VerifiedExpenses,
        TimeZoneInfo TimeZone,
        string Platform,
        string Payment);

    private sealed class PlatformTotals
    {
        public decimal Net { get; set; }
        public decimal Fees { get; set; }
        public decimal Cash { get; set; }
        public decimal Card { get; set; }
        public double Hours { get; set; }
        public double Km { get; set; }
        public int Rides { get; set; }
    }

    private sealed record PeriodTotals(
        PlatformTotals Bolt,
        PlatformTotals Uber,
        decimal DeductibleExpenses,
        bool HasAnyActivity)
    {
        public decimal Net => Bolt.Net + Uber.Net;
        public decimal Fees => Bolt.Fees + Uber.Fees;
        public double Hours => Bolt.Hours + Uber.Hours;
        public double Km => Bolt.Km + Uber.Km;
        public int Rides => Bolt.Rides + Uber.Rides;
    }

    private static PeriodTotals Aggregate(AggregationContext ctx, PfaDashboardPeriod period)
    {
        (DateTime startUtc, DateTime endUtc) = period.ToUtcBounds(ctx.TimeZone);

        var bolt = new PlatformTotals();
        var orders = ctx.BoltOrders
            .Where(o => o.OrderCreatedTime >= startUtc && o.OrderCreatedTime < endUtc)
            .Where(o => MatchesPayment(o, ctx.Payment))
            .ToList();

        foreach (BoltOrder order in orders)
        {
            bolt.Net += order.NetEarnings;
            bolt.Fees += order.Commission;
            if (IsCash(order))
            {
                bolt.Cash += order.NetEarnings;
            }
            else
            {
                bolt.Card += order.NetEarnings;
            }

            bolt.Km += order.RideDistance / 1000.0;
            bolt.Hours += RideHours(order);
            bolt.Rides++;
        }

        var uber = new PlatformTotals();
        foreach (PfaDashboardPeriod.MonthSlice slice in period.SliceMonths())
        {
            AddUberMonth(uber, ctx, slice);
        }

        decimal expenses = 0m;
        foreach (PfaDashboardPeriod.MonthSlice slice in period.SliceMonths())
        {
            expenses += slice.Weight * ctx.VerifiedExpenses
                .Where(e => e.Year == slice.Year && e.Month == slice.Month)
                .Sum(e => e.Amount);
        }

        bool hasActivity = orders.Count > 0 || uber.Net != 0m || uber.Rides > 0;

        return new PeriodTotals(bolt, uber, expenses, hasActivity);
    }

    /// <summary>
    /// Uber nu are curse individuale, doar totaluri pe lună, împărțite pe trei tipuri de raport
    /// (earnings / hours / trips). O lună acoperită parțial intră proporțional cu zilele.
    /// </summary>
    private static void AddUberMonth(PlatformTotals uber, AggregationContext ctx, PfaDashboardPeriod.MonthSlice slice)
    {
        var monthImports = ctx.UberImports
            .Where(i => i.Year == slice.Year && i.Month == slice.Month)
            .ToList();

        if (monthImports.Count == 0)
        {
            return;
        }

        decimal net = monthImports.Where(IsEarnings).Sum(i => i.NetEarnings);
        decimal fees = monthImports.Where(IsEarnings).Sum(i => i.Commission);
        decimal cash = monthImports.Where(IsEarnings).Sum(i => i.CashCollected);
        double hours = monthImports.Where(IsHours).Sum(i => i.OnlineHours);
        double km = monthImports.Where(IsTrips).Sum(i => i.Kilometers);
        int rides = monthImports.Where(IsHours).Sum(i => i.Trips);

        // Filtrul de tip plată nu poate coborî sub nivelul lunii: se aplică proporțional,
        // pe ponderea numerarului în încasările nete raportate de Uber.
        decimal paymentShare = ctx.Payment switch
        {
            "cash" => net > 0 ? cash / net : 0m,
            "card" => net > 0 ? Math.Max(0m, net - cash) / net : 0m,
            _ => 1m
        };

        decimal weight = slice.Weight * paymentShare;
        if (weight <= 0m)
        {
            return;
        }

        uber.Net += weight * net;
        uber.Fees += weight * fees;
        uber.Cash += ctx.Payment == "card" ? 0m : slice.Weight * cash;
        uber.Card += ctx.Payment == "cash" ? 0m : slice.Weight * Math.Max(0m, net - cash);
        uber.Hours += (double)weight * hours;
        uber.Km += (double)weight * km;
        uber.Rides += (int)Math.Round(weight * rides, MidpointRounding.AwayFromZero);
    }

    /* ── Taxe ─────────────────────────────────────────────────────────────── */

    /// <summary>
    /// Rezerva fiscală a perioadei. Aritmetica trăiește în <see cref="TaxReserveCalculator"/>,
    /// unde poate fi testată fără bază de date; aici rămâne doar despachetarea totalurilor.
    /// </summary>
    private TaxReserveResult BuildReserve(
        PeriodTotals totals,
        decimal annualIncome,
        PfaTaxCalculator.TaxResult annualTaxes) =>
        TaxReserveCalculator.Compute(
            totals.Fees,
            totals.Bolt.Fees,
            totals.Net,
            annualIncome,
            annualTaxes,
            _fiscal);

    /* ── KPI ──────────────────────────────────────────────────────────────── */

    private static PfaDashboardKpisResponse BuildKpis(PeriodTotals current, PeriodTotals previous)
    {
        decimal? PreviousOf(Func<PeriodTotals, decimal> selector) =>
            previous.HasAnyActivity ? Round(selector(previous)) : null;

        decimal netPerHour = current.Hours > 0 ? current.Net / (decimal)current.Hours : 0m;
        decimal netPerKm = current.Km > 0 ? current.Net / (decimal)current.Km : 0m;
        decimal previousNetPerHour = previous.Hours > 0 ? previous.Net / (decimal)previous.Hours : 0m;
        decimal previousNetPerKm = previous.Km > 0 ? previous.Net / (decimal)previous.Km : 0m;

        return new PfaDashboardKpisResponse(
            new PfaDashboardMetric(Round(current.Net), PreviousOf(t => t.Net)),
            new PfaDashboardFeeMetric(
                Round(current.Fees),
                PreviousOf(t => t.Fees),
                new PfaPlatformAmounts(Round(current.Bolt.Fees), Round(current.Uber.Fees))),
            new PfaDashboardMetric(
                Math.Round((decimal)current.Hours, 1),
                previous.HasAnyActivity ? Math.Round((decimal)previous.Hours, 1) : null),
            new PfaDashboardMetric(
                Math.Round((decimal)current.Km, 1),
                previous.HasAnyActivity ? Math.Round((decimal)previous.Km, 1) : null),
            new PfaDashboardMetric(Round(netPerHour), previous.HasAnyActivity ? Round(previousNetPerHour) : null),
            new PfaDashboardMetric(Round(netPerKm), previous.HasAnyActivity ? Round(previousNetPerKm) : null));
    }

    private static List<PfaPlatformSplitResponse> BuildPlatformSplit(PeriodTotals totals, string platform)
    {
        List<PfaPlatformSplitResponse> split = [];

        if (platform != "uber")
        {
            split.Add(new PfaPlatformSplitResponse(
                "bolt",
                Round(totals.Bolt.Net),
                Round(totals.Bolt.Fees),
                Round(totals.Bolt.Cash),
                Round(totals.Bolt.Card),
                totals.Bolt.Rides));
        }

        if (platform != "bolt")
        {
            split.Add(new PfaPlatformSplitResponse(
                "uber",
                Round(totals.Uber.Net),
                Round(totals.Uber.Fees),
                Round(totals.Uber.Cash),
                Round(totals.Uber.Card),
                totals.Uber.Rides));
        }

        return split;
    }

    /* ── Serii ────────────────────────────────────────────────────────────── */

    private PfaDashboardSeriesResponse BuildSeries(
        AggregationContext ctx,
        PfaDashboardPeriod period,
        string granularity,
        PeriodTotals totals,
        TaxReserveResult reserve)
    {
        List<PfaDashboardPeriod> buckets = granularity == "day"
            ? Enumerable.Range(0, period.DayCount)
                .Select(offset =>
                {
                    DateOnly day = period.From.AddDays(offset);
                    return new PfaDashboardPeriod(day, day);
                })
                .ToList()
            : period.SliceMonths()
                .Select(slice =>
                {
                    var start = new DateOnly(slice.Year, slice.Month, 1);
                    DateOnly monthEnd = start.AddMonths(1).AddDays(-1);
                    return new PfaDashboardPeriod(
                        start < period.From ? period.From : start,
                        monthEnd > period.To ? period.To : monthEnd);
                })
                .ToList();

        List<PfaNetEarningsPointResponse> netSeries = [];
        List<PfaFeesAndTaxesPointResponse> feeSeries = [];
        List<PfaRealProfitPointResponse> profitSeries = [];

        // Cheltuielile și taxele nu au dată zilnică; se împart pe bucket-uri proporțional
        // cu încasările, ca linia de profit să urmeze forma reală a activității.
        foreach (PfaDashboardPeriod bucket in buckets)
        {
            PeriodTotals bucketTotals = Aggregate(ctx, bucket);
            string key = bucket.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string label = granularity == "day"
                ? bucket.From.Day.ToString(CultureInfo.InvariantCulture)
                : RomanianCulture.DateTimeFormat.AbbreviatedMonthNames[bucket.From.Month - 1];

            decimal vat = _fiscal.VatIntracomRate * bucketTotals.Fees;
            decimal nonResident = _fiscal.BoltNonResidentRate * bucketTotals.Bolt.Fees;
            decimal share = totals.Net > 0 ? bucketTotals.Net / totals.Net : 0m;

            netSeries.Add(new PfaNetEarningsPointResponse(
                key,
                label,
                Round(bucketTotals.Bolt.Net),
                Round(bucketTotals.Uber.Net),
                Round(bucketTotals.Net),
                bucketTotals.Rides));

            feeSeries.Add(new PfaFeesAndTaxesPointResponse(
                key,
                label,
                Round(bucketTotals.Bolt.Fees),
                Round(bucketTotals.Uber.Fees),
                Round(vat),
                Round(nonResident)));

            decimal bucketExpenses = totals.DeductibleExpenses * share;
            decimal bucketTaxes = reserve.Total * share;

            profitSeries.Add(new PfaRealProfitPointResponse(
                key,
                label,
                Round(bucketTotals.Net),
                Round(bucketExpenses),
                Round(bucketTaxes),
                Round(bucketTotals.Net - bucketExpenses - bucketTaxes)));
        }

        return new PfaDashboardSeriesResponse(netSeries, feeSeries, profitSeries);
    }

    /// <summary>
    /// Contul de flotă a fost configurat în onboarding, dar platforma nu l-a activat încă. Nu e
    /// nici „conectat", nici „lipsă" — e în procesare, iar UI-ul trebuie să spună asta.
    /// </summary>
    private static bool IsAwaitingActivation(
        List<PfaPlatformAccount> accounts,
        PfaPlatformProvider provider)
    {
        PfaPlatformAccount? account = accounts.FirstOrDefault(a => a.Provider == provider);

        return account is not null
            && account.OnboardingStatus is not (PfaPlatformOnboardingStatus.NotStarted
                or PfaPlatformOnboardingStatus.Skipped);
    }

    private static PfaUberSourceResponse BuildUberSource(List<UberCsvImport> imports)
    {
        if (imports.Count == 0)
        {
            return new PfaUberSourceResponse(false, null, null);
        }

        UberCsvImport last = imports.MaxBy(i => i.ImportedAtUtc)!;
        var lastPeriodImports = imports
            .Where(i => i.Year == last.Year && i.Month == last.Month)
            .ToList();

        var rangeStart = new DateOnly(last.Year, last.Month, 1);
        DateOnly rangeEnd = rangeStart.AddMonths(1).AddDays(-1);

        return new PfaUberSourceResponse(
            true,
            DateOnly.FromDateTime(PfaDashboardPeriod.NormalizeUtc(last.ImportedAtUtc)),
            lastPeriodImports.Count > 0
                ? $"{rangeStart:yyyy-MM-dd}/{rangeEnd:yyyy-MM-dd}"
                : null);
    }

    /* ── Helpers ──────────────────────────────────────────────────────────── */

    private static bool IsEarnings(UberCsvImport import) =>
        string.Equals(import.FileType, "earnings", StringComparison.OrdinalIgnoreCase);

    private static bool IsHours(UberCsvImport import) =>
        string.Equals(import.FileType, "hours", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrips(UberCsvImport import) =>
        string.Equals(import.FileType, "trips", StringComparison.OrdinalIgnoreCase);

    private static bool IsCash(BoltOrder order) =>
        order.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPayment(BoltOrder order, string payment) => payment switch
    {
        "cash" => IsCash(order),
        "card" => !IsCash(order),
        _ => true
    };

    private static double RideHours(BoltOrder order)
    {
        if (!order.OrderFinishedTime.HasValue)
        {
            return 0;
        }

        TimeSpan duration = PfaDashboardPeriod.NormalizeUtc(order.OrderFinishedTime.Value)
            - PfaDashboardPeriod.NormalizeUtc(order.OrderCreatedTime);

        return duration.TotalHours > 0 ? duration.TotalHours : 0;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static string NormalizePlatform(string? platform) =>
        platform?.Trim().ToUpperInvariant() switch
        {
            "BOLT" => "bolt",
            "UBER" => "uber",
            _ => "all"
        };

    internal static string NormalizePayment(string? payment) =>
        payment?.Trim().ToUpperInvariant() switch
        {
            "CASH" => "cash",
            "CARD" => "card",
            _ => "all"
        };
}
