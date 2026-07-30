using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations;
using Application.PfaRegistrations.FiscalProfile;
using Domain.Documents;
using Domain.Expenses;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.GetDashboardSummary;

internal sealed class GetDashboardSummaryQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    public async Task<Result<DashboardSummaryResponse>> Handle(
        GetDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        // PFA registration (most recent)
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // All documents for this user
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == query.UserId)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        int total = documents.Count;
        int verified = documents.Count(d => d.Status == DocumentStatus.Verified);
        int pending = documents.Count(d => d.Status == DocumentStatus.Pending);
        int rejected = documents.Count(d => d.Status == DocumentStatus.Rejected);

        // Unread notifications
        int unread = await context.Notifications
            .Where(n => n.UserId == query.UserId && !n.IsRead)
            .CountAsync(cancellationToken);

        // 5 most recently uploaded documents
        var recent = documents
            .Take(5)
            .Select(d => new RecentDocumentDto(
                d.Id,
                d.OriginalFileName,
                d.Category.ToString(),
                d.Status.ToString(),
                d.UploadedAtUtc))
            .ToList();

        DateTime now = DateTime.UtcNow;
        int incomeYear = now.Year;
        int incomeMonth = now.Month;

        PfaMonthlyIncome? monthlyIncome = pfa is null
            ? null
            : await context.PfaMonthlyIncomes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    i => i.PfaRegistrationId == pfa.Id && i.Year == incomeYear && i.Month == incomeMonth,
                    cancellationToken);

        decimal? venitTotal = monthlyIncome?.ComputeVenitTotal();

        // All monthly incomes for the revenue chart
        List<MonthlyRevenuePointDto> monthlyRevenue = [];
        List<PfaMonthlyIncome> yearIncomes = [];

        if (pfa is not null)
        {
            yearIncomes = await context.PfaMonthlyIncomes
                .AsNoTracking()
                .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == incomeYear)
                .ToListAsync(cancellationToken);

            monthlyRevenue = Enumerable.Range(1, 12)
                .Select(month =>
                {
                    PfaMonthlyIncome? row = yearIncomes.FirstOrDefault(i => i.Month == month);
                    return new MonthlyRevenuePointDto(
                        month,
                        row?.ComputeVenitTotal() ?? 0,
                        row?.VenitCash ?? 0,
                        row?.VenitCard ?? 0,
                        row?.VenitBolt ?? 0,
                        row?.VenitUber ?? 0);
                })
                .ToList();
        }

        DashboardPeriodStatsDto monthlyStats = new(
            incomeYear,
            incomeMonth,
            monthlyIncome?.VenitCash ?? 0,
            monthlyIncome?.VenitCard ?? 0,
            monthlyIncome?.VenitBolt ?? 0,
            monthlyIncome?.VenitUber ?? 0,
            monthlyIncome?.ComputeVenitTotal() ?? 0);

        DashboardPeriodStatsDto yearlyStats = new(
            incomeYear,
            null,
            yearIncomes.Sum(i => i.VenitCash),
            yearIncomes.Sum(i => i.VenitCard),
            yearIncomes.Sum(i => i.VenitBolt),
            yearIncomes.Sum(i => i.VenitUber),
            yearIncomes.Sum(i => i.ComputeVenitTotal()));

        // ── YTD Tax Computation ─────────────────────────────────────────────────
        decimal ytdTotalIncome = 0m;
        decimal ytdDeductibleExpenses = 0m;
        List<YtdExpenseDto> ytdExpenses = [];
        PfaTaxCalculator.TaxResult taxResult = PfaTaxCalculator.Compute(0m, 0m, incomeYear);

        if (pfa is not null)
        {
            // Sum all monthly income for the year
            ytdTotalIncome = yearIncomes.Sum(i => i.ComputePlatformIncome());

            // Fetch all deductible expenses for the year with their document status
            var expensesWithStatus = await context.DeductibleExpenses
                .AsNoTracking()
                .Where(e => e.PfaRegistrationId == pfa.Id && e.Year == incomeYear)
                .Join(
                    context.Documents.AsNoTracking(),
                    e => e.DocumentId,
                    d => d.Id,
                    (e, d) => new
                    {
                        e.Id,
                        e.ItemName,
                        e.CatalogCategory,
                        e.DeductibleLabel,
                        e.AmountRon,
                        e.Month,
                        d.Status
                    })
                .ToListAsync(cancellationToken);

            ytdExpenses = expensesWithStatus
                .Select(e => new YtdExpenseDto(
                    e.Id,
                    e.ItemName,
                    e.CatalogCategory,
                    e.DeductibleLabel,
                    e.AmountRon,
                    e.Month,
                    e.Status.ToString()))
                .ToList();

            // Only verified expenses reduce the tax base
            ytdDeductibleExpenses = expensesWithStatus
                .Where(e => e.Status == DocumentStatus.Verified)
                .Sum(e => e.AmountRon ?? 0m);

            // Run the tax formula
            taxResult = PfaTaxCalculator.Compute(ytdTotalIncome, ytdDeductibleExpenses, incomeYear);
        }

        PfaTaxCalculator.TaxThresholdProgress thresholdProgress =
            PfaTaxCalculator.ComputeThresholdProgress(ytdTotalIncome, ytdDeductibleExpenses, incomeYear);

        var taxThresholds = new TaxThresholdsDto(
            thresholdProgress.Profit,
            thresholdProgress.CasFirstThreshold,
            thresholdProgress.CasSecondThreshold,
            thresholdProgress.CassFirstThreshold,
            thresholdProgress.CassMaximumThreshold,
            thresholdProgress.RemainingToNextCasThreshold,
            thresholdProgress.RemainingToNextCassThreshold,
            thresholdProgress.HasReachedCasFirstThreshold,
            thresholdProgress.HasReachedCasSecondThreshold,
            thresholdProgress.HasReachedCassFirstThreshold,
            thresholdProgress.HasReachedCassMaximumThreshold);

        PfaFiscalSettingsResponse? fiscalSettings = null;
        if (pfa is not null)
        {
            PfaFiscalProfile? profile = await context.PfaFiscalProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(fp => fp.PfaRegistrationId == pfa.Id, cancellationToken);

            List<PfaPlatformAccount> accounts = await context.PfaPlatformAccounts
                .AsNoTracking()
                .Where(a => a.PfaRegistrationId == pfa.Id)
                .OrderBy(a => a.Kind)
                .ThenBy(a => a.Provider)
                .ToListAsync(cancellationToken);

            PfaFleetConsent? consent = await context.PfaFleetConsents
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.PfaRegistrationId == pfa.Id, cancellationToken);

            fiscalSettings = new PfaFiscalSettingsResponse(
                profile is null
                    ? PfaFiscalProfileMapper.DefaultProfile(pfa.Id)
                    : PfaFiscalProfileMapper.MapProfile(profile),
                accounts.Select(PfaFiscalProfileMapper.MapAccount).ToList(),
                consent is null
                    ? PfaFiscalProfileMapper.DefaultConsent(pfa.Id)
                    : PfaFiscalProfileMapper.MapConsent(consent));
        }

        return new DashboardSummaryResponse(
            pfa?.Id,
            pfa?.Status.ToString(),
            pfa?.RegistrationType.ToString(),
            pfa?.Cui,
            documents.FirstOrDefault(d => d.Category == DocumentCategory.CertificatInregistrare)?.Id,
            pfa?.CreatedAtUtc,
            total,
            verified,
            pending,
            rejected,
            unread,
            recent,
            monthlyIncome?.VenitCash,
            monthlyIncome?.VenitCard,
            monthlyIncome?.VenitBolt,
            monthlyIncome?.VenitUber,
            monthlyIncome?.TaxeEstimate,
            venitTotal,
            monthlyIncome is null ? null : incomeYear,
            monthlyIncome is null ? null : incomeMonth,
            monthlyStats,
            yearlyStats,
            incomeYear,
            monthlyRevenue,
            // YTD tax breakdown
            incomeYear,
            ytdTotalIncome,
            ytdDeductibleExpenses,
            taxResult.Profit,
            taxResult.Cas,
            taxResult.Cass,
            taxResult.IncomeTax,
            taxResult.TotalTax,
            taxResult.NetIncome,
            ytdExpenses,
            taxThresholds,
            fiscalSettings);
    }
}
