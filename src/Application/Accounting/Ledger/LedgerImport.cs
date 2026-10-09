using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>
/// Importul ledger-ului unui PFA: rulează sursele în ordine (bancă, platforme, Oblio) și salvează
/// după fiecare. Rulat zilnic de job, pentru toate PFA-urile active (B6); pentru un dosar inactiv,
/// doar operațiunile de până la încheierea colaborării (B8).
/// </summary>
public sealed record RunLedgerImportCommand(Guid PfaId, bool FiscalLinkOnly = false) : ICommand<IReadOnlyList<LedgerImportResult>>;

internal sealed class RunLedgerImportCommandHandler(
    IApplicationDbContext db,
    IEnumerable<ILedgerSource> sources,
    IOptions<AccountingOptions> options)
    : ICommandHandler<RunLedgerImportCommand, IReadOnlyList<LedgerImportResult>>
{
    public async Task<Result<IReadOnlyList<LedgerImportResult>>> Handle(RunLedgerImportCommand command, CancellationToken cancellationToken)
    {
        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == command.PfaId)
            .Select(p => new { p.Id, p.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (pfa is null)
        {
            return Result.Failure<IReadOnlyList<LedgerImportResult>>(AccountingErrors.PfaNotFound);
        }

        // PFAlone își completează singur registrele: nimic nu intră automat în ele.
        if (await Payments.PlanAccess.ManagesOwnBooksAsync(db, pfa.UserId, cancellationToken))
        {
            return Result.Success<IReadOnlyList<LedgerImportResult>>([]);
        }

        // Doar o colaborare înregistrată limitează importul: de la începutul ei și, după inactivare,
        // până la data încheierii (B8). Fără rând, se importă tot istoricul disponibil.
        PfaAccountingEngagement? engagement = await Pfas.PfaEngagements.LatestAsync(db, pfa.Id, cancellationToken);
        DateOnly? from = engagement?.StartDate;
        DateOnly? to = engagement?.Status == EngagementStatus.Inactive ? engagement.EndDate : null;

        var context = new LedgerImportContext(
            pfa.Id,
            pfa.UserId,
            from,
            await LedgerSupport.RulesAsync(db, pfa.Id, cancellationToken),
            await LedgerSupport.ClosedPeriodsAsync(db, pfa.Id, cancellationToken),
            options.Value,
            to);

        var results = new List<LedgerImportResult>();
        foreach (ILedgerSource source in sources.Where(s => !command.FiscalLinkOnly || s is FiscalLinkLedgerSource).OrderBy(s => s.Order))
        {
            results.Add(await source.ImportAsync(context, cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }

        return results;
    }
}

/// <summary>PFA-urile pe care importul zilnic le parcurge: cele din scope-ul contabil al lunii.</summary>
public sealed record ListLedgerImportPfasQuery(string Period) : IQuery<IReadOnlyList<Guid>>;

internal sealed class ListLedgerImportPfasQueryHandler(IApplicationDbContext db) : IQueryHandler<ListLedgerImportPfasQuery, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(ListLedgerImportPfasQuery query, CancellationToken cancellationToken) =>
        (await Months.AccountingScope.InPeriodAsync(db, query.Period, cancellationToken)).Select(pfa => pfa.Id).ToList();
}
