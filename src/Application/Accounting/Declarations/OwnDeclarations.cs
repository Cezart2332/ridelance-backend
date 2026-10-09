using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Annual;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Application.Accounting.Months;
using Application.Accounting.Pfas;
using Application.Accounting.Tax;
using Application.Payments;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>O declarație generată de titularul PFAlone, cu XML-ul de depus el însuși.</summary>
/// <param name="XmlDocumentId">XML-ul pentru ANAF; anualele nu au (se completează în formularul ANAF).</param>
public sealed record OwnDeclarationDto(
    Guid DeclarationId,
    DeclarationType Type,
    string Period,
    decimal Amount,
    DateOnly? DueDate,
    DeclarationStatus Status,
    Guid? XmlDocumentId);

/// <summary>Rezultatul unei generări: ce s-a făcut sau de ce nu se poate.</summary>
public sealed record OwnGenerationResult(bool Generated, string Message);

/// <summary><c>GET /pfa/accounting/own-declarations?year=</c> — declarațiile generate de titular.</summary>
public sealed record GetOwnDeclarationsQuery(int Year) : IQuery<IReadOnlyList<OwnDeclarationDto>>;

/// <summary>
/// <c>POST /pfa/accounting/own-declarations/monthly</c> — D100, D301 și D390 ale unei luni, din
/// documentele lunii încărcate de titular. D700 nu e aici: ține de onboarding.
/// </summary>
public sealed record GenerateOwnMonthlyDeclarationsCommand(string Period) : ICommand<OwnGenerationResult>;

/// <summary><c>POST /pfa/accounting/own-declarations/annual</c> — anualele (D212 și ce se aplică), din registrele lui.</summary>
public sealed record GenerateOwnAnnualDeclarationsCommand(int Year) : ICommand<OwnGenerationResult>;

/// <summary>
/// Generatorul PFAlone. Același calcul și același XML ca la contabil (<see cref="DeclarationGeneration"/>,
/// <see cref="AnnualDeclarationService"/>), pe datele pe care le-a trecut titularul. Depunerea e a lui.
/// </summary>
internal static class OwnDeclarations
{
    public static readonly Error NotSelfManaged = Error.Problem(
        "OwnDeclarations.NotSelfManaged",
        "Generatorul de declarații e inclus în PFAlone. La PFA Full declarațiile le pregătește contabilul.");

    public static readonly Error InvalidPeriod = Error.Problem("OwnDeclarations.InvalidPeriod", "Luna aleasă nu este validă.");

    public static readonly Error MissingCui = Error.Problem(
        "OwnDeclarations.MissingCui",
        "Completează CIF-ul PFA-ului în profil: fără el declarațiile nu se pot genera.");

    /// <summary>Dosarul PFA al omului, doar dacă e PFAlone.</summary>
    public static async Task<Result<ScopePfa>> OwnPfaAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        if (!await PlanAccess.ManagesOwnBooksAsync(db, userId, cancellationToken))
        {
            return Result.Failure<ScopePfa>(NotSelfManaged);
        }

        if (await ClientLedger.PfaIdAsync(db, userId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<ScopePfa>(ClientLedger.NoPfa);
        }

        var row = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.Id, p.LegalName, p.HolderName, p.FullName, p.Cui, p.User.FirstName, p.User.LastName })
            .SingleAsync(cancellationToken);
        return new ScopePfa(row.Id, PfaNames.Of(row.LegalName, row.HolderName, row.FullName, row.FirstName, row.LastName), row.Cui ?? string.Empty);
    }
}

internal sealed class GetOwnDeclarationsQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<GetOwnDeclarationsQuery, IReadOnlyList<OwnDeclarationDto>>
{
    public async Task<Result<IReadOnlyList<OwnDeclarationDto>>> Handle(GetOwnDeclarationsQuery query, CancellationToken cancellationToken)
    {
        Result<ScopePfa> pfa = await OwnDeclarations.OwnPfaAsync(db, userContext.UserId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<IReadOnlyList<OwnDeclarationDto>>(pfa.Error);
        }

        string prefix = query.Year.ToString(CultureInfo.InvariantCulture);
        var versions = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfa.Value.Id && v.Declaration.Period.StartsWith(prefix) &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
            .Select(v => new { v.DeclarationId, v.Declaration.Type, v.Declaration.Period, v.Amount, v.Status, v.XmlDocumentId })
            .ToListAsync(cancellationToken);
        TaxRuleSet rules = await TaxRuleSet.LoadAsync(db, cancellationToken);

        return versions
            .OrderByDescending(v => v.Period, StringComparer.Ordinal)
            .ThenBy(v => v.Type)
            .Select(v => new OwnDeclarationDto(
                v.DeclarationId, v.Type, v.Period, v.Amount, GetClientDeclarationsQueryHandler.DueDate(rules, v.Type, v.Period), v.Status, v.XmlDocumentId))
            .ToList();
    }
}

internal sealed class GenerateOwnMonthlyDeclarationsCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    ICommandHandler<RunPlatformDocumentExtractionCommand> extraction,
    DeclarationFiles files)
    : ICommandHandler<GenerateOwnMonthlyDeclarationsCommand, OwnGenerationResult>
{
    public async Task<Result<OwnGenerationResult>> Handle(GenerateOwnMonthlyDeclarationsCommand command, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact($"{command.Period}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Result.Failure<OwnGenerationResult>(OwnDeclarations.InvalidPeriod);
        }

        if (!MonthlyDeclarationPeriod.IsCompleted(command.Period, DateTime.UtcNow))
        {
            return new OwnGenerationResult(false, MonthlyDeclarationPeriod.Message);
        }

        Result<ScopePfa> owned = await OwnDeclarations.OwnPfaAsync(db, userContext.UserId, cancellationToken);
        if (owned.IsFailure)
        {
            return Result.Failure<OwnGenerationResult>(owned.Error);
        }

        ScopePfa pfa = owned.Value;
        if (!pfa.HasCui)
        {
            return Result.Failure<OwnGenerationResult>(OwnDeclarations.MissingCui);
        }

        // Ca pasul „process” al contabilului: documentele lunii rămase în coadă se citesc acum.
        List<Guid> queued = await db.PlatformDocuments
            .Where(d => d.PfaRegistrationId == pfa.Id && d.Period == command.Period && d.Status == PlatformDocumentStatus.Extracting)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid id in queued)
        {
            await extraction.Handle(new RunPlatformDocumentExtractionCommand(id), cancellationToken);
        }

        await NonResident.NonResidentSync.SyncAsync(db, pfa.Id, cancellationToken);
        var settings = TaxEngineSettings.ForPeriod(await TaxRuleSet.LoadAsync(db, cancellationToken), command.Period);
        MonthData data = await MonthData.LoadAsync(db, command.Period, [pfa.Id], cancellationToken);

        (bool ok, string message) = await DeclarationGeneration.GenerateAsync(db, files, pfa, data, settings, userContext.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new OwnGenerationResult(ok, message);
    }
}

internal sealed class GenerateOwnAnnualDeclarationsCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    AnnualDeclarationService annual)
    : ICommandHandler<GenerateOwnAnnualDeclarationsCommand, OwnGenerationResult>
{
    public async Task<Result<OwnGenerationResult>> Handle(GenerateOwnAnnualDeclarationsCommand command, CancellationToken cancellationToken)
    {
        if (!AnnualDtos.IsValidYear(command.Year))
        {
            return Result.Failure<OwnGenerationResult>(AnnualDtos.InvalidYear);
        }

        Result<ScopePfa> owned = await OwnDeclarations.OwnPfaAsync(db, userContext.UserId, cancellationToken);
        if (owned.IsFailure)
        {
            return Result.Failure<OwnGenerationResult>(owned.Error);
        }

        IReadOnlyList<DeclarationType> generated = await annual.GenerateAsync(owned.Value.Id, command.Year, userContext.UserId, cancellationToken);
        if (generated.Count > 0)
        {
            return new OwnGenerationResult(true, $"Generate: {string.Join(", ", generated)}.");
        }

        // Nimic nou: fie existau deja, fie D212 are blocaje. Spunem primul blocaj, e ce are de rezolvat.
        AnnualData data = await annual.LoadAsync(owned.Value.Id, command.Year, cancellationToken);
        IReadOnlyList<string> blockers = data.BlockersOf(DeclarationType.D212);
        return blockers.Count > 0
            ? new OwnGenerationResult(false, blockers[0])
            : new OwnGenerationResult(true, "Declarațiile anului existau deja.");
    }
}
