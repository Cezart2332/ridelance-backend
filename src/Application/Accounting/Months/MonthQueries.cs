using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Months;

/// <summary><c>GET /accounting/periods/{period}/overview</c></summary>
public sealed record GetPeriodOverviewQuery(string Period) : IQuery<PeriodOverview>;

/// <summary>
/// Privirea de ansamblu a lunii (spec B3): toate PFA-urile, dintr-un număr fix de interogări —
/// PFA-urile, documentele, setările și regulile, pre-check-urile, declarațiile — nu câte una pe PFA.
/// </summary>
internal sealed class GetPeriodOverviewQueryHandler(IApplicationDbContext db, IOptions<AccountingOptions> options)
    : IQueryHandler<GetPeriodOverviewQuery, PeriodOverview>
{
    public async Task<Result<PeriodOverview>> Handle(GetPeriodOverviewQuery query, CancellationToken cancellationToken)
    {
        if (!Documents.PlatformDocumentSupport.IsValidPeriod(query.Period))
        {
            return Result.Failure<PeriodOverview>(AccountingErrors.InvalidPeriod);
        }

        List<ScopePfa> pfas = await AccountingScope.InPeriodAsync(db, query.Period, cancellationToken);
        List<Guid> ids = [.. pfas.Select(p => p.Id)];
        MonthData data = await MonthData.LoadAsync(db, query.Period, ids, cancellationToken);
        Dictionary<Guid, PfaMonthCheck> checks = await db.PfaMonthChecks.AsNoTracking()
            .Where(c => c.Period == query.Period && ids.Contains(c.PfaRegistrationId))
            .ToDictionaryAsync(c => c.PfaRegistrationId, cancellationToken);
        List<DeclarationSummaries.CurrentVersion> versions = await DeclarationSummaries.CurrentVersionsAsync(db, query.Period, ids, cancellationToken);
        var settings = TaxEngineSettings.From(options.Value);

        List<OverviewRow> rows = [.. pfas.Select(pfa =>
        {
            PfaMonthCheck? check = checks.GetValueOrDefault(pfa.Id);
            IReadOnlyList<DeclarationSummary> summaries = DeclarationSummaries.For(
                pfa, query.Period, check, versions, () => MonthlyTaxEngine.Calculate(data.TaxInput(pfa.Id, settings)));
            return new OverviewRow(
                pfa.Id,
                pfa.Name,
                pfa.Cui,
                check?.Status ?? PfaMonthStatus.NotProcessed,
                check is null ? [] : AccountingJson.Deserialize<List<string>>(check.ReasonsJson, []),
                Figures(data, pfa.Id, Platform.Bolt),
                Figures(data, pfa.Id, Platform.Uber),
                summaries.ToDictionary(
                    summary => summary.Type,
                    summary => new DeclarationCell(summary.DeclarationId, summary.CurrentVersionId, summary.Status, summary.Amount)));
        })];

        int Count(PfaMonthStatus status) => rows.Count(row => row.Status == status);
        return new PeriodOverview(
            query.Period,
            new PeriodStats(rows.Count, Count(PfaMonthStatus.Ready), Count(PfaMonthStatus.NeedsReview), Count(PfaMonthStatus.MissingDocuments), Count(PfaMonthStatus.NotProcessed)),
            rows);
    }

    /// <summary>Venitul din raport și comisionul din factură (sau din raport), dacă PFA-ul lucrează cu platforma.</summary>
    private static PlatformMonthFigures? Figures(MonthData data, Guid pfaId, Platform platform)
    {
        if (data.PlatformsOf(pfaId) is { } platforms && !platforms.Contains(platform))
        {
            return null;
        }

        List<MonthDocument> documents = [.. data.DocumentsOf(pfaId).Where(d => d.Document.Platform == platform)];
        DocumentExtraction? report = documents.FirstOrDefault(d => d.Document.DocumentType == PlatformDocumentType.PlatformReport)?.Extraction;
        DocumentExtraction? invoice = documents.FirstOrDefault(d => d.Document.DocumentType == PlatformDocumentType.CommissionInvoice)?.Extraction;
        return new PlatformMonthFigures(report?.Amount, invoice?.CommissionAmount ?? report?.CommissionAmount);
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/declarations?period=</c> — D100, D301, D390.</summary>
public sealed record ListDeclarationsQuery(Guid PfaId, string Period) : IQuery<IReadOnlyList<DeclarationSummary>>;

internal sealed class ListDeclarationsQueryHandler(IApplicationDbContext db, IOptions<AccountingOptions> options)
    : IQueryHandler<ListDeclarationsQuery, IReadOnlyList<DeclarationSummary>>
{
    public async Task<Result<IReadOnlyList<DeclarationSummary>>> Handle(ListDeclarationsQuery query, CancellationToken cancellationToken)
    {
        if (!Documents.PlatformDocumentSupport.IsValidPeriod(query.Period))
        {
            return Result.Failure<IReadOnlyList<DeclarationSummary>>(AccountingErrors.InvalidPeriod);
        }

        var registration = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == query.PfaId)
            .Select(p => new { p.Id, p.LegalName, p.FullName, p.Cui })
            .SingleOrDefaultAsync(cancellationToken);
        if (registration is null)
        {
            return Result.Failure<IReadOnlyList<DeclarationSummary>>(AccountingErrors.PfaNotFound);
        }

        var pfa = new ScopePfa(registration.Id, registration.LegalName ?? registration.FullName ?? string.Empty, registration.Cui ?? string.Empty);
        PfaMonthCheck? check = await db.PfaMonthChecks.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PfaRegistrationId == pfa.Id && c.Period == query.Period, cancellationToken);
        List<DeclarationSummaries.CurrentVersion> versions = await DeclarationSummaries.CurrentVersionsAsync(db, query.Period, [pfa.Id], cancellationToken);
        MonthData? data = check?.Status == PfaMonthStatus.Ready && versions.Count == 0
            ? await MonthData.LoadAsync(db, query.Period, [pfa.Id], cancellationToken)
            : null;
        var settings = TaxEngineSettings.From(options.Value);

        return Result.Success(DeclarationSummaries.For(pfa, query.Period, check, versions, () => MonthlyTaxEngine.Calculate(data!.TaxInput(pfa.Id, settings))));
    }
}
