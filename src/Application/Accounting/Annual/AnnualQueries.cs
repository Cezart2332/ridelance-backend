using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Annual;

/// <summary>Înregistrarea unei declarații anuale, versiunea curentă.</summary>
public sealed record AnnualRecordDto(Guid DeclarationId, Guid VersionId, int VersionNo, DeclarationStatus Status, decimal Amount);

/// <summary>Un rând din ecranul anual: ce se depune, sumele, blocajele și înregistrarea, dacă există.</summary>
public sealed record AnnualDeclarationDto<T>(DeclarationType Type, bool Ready, IReadOnlyList<string> Blockers, IReadOnlyList<string> Review, T Model, AnnualRecordDto? Record);

/// <summary>D212: rezultatul anual, starea răspunsului despre alte venituri și comparația cu precompletarea.</summary>
public sealed record D212Dto(
    D212DataModel? Model,
    IReadOnlyList<D212Field> Form,
    string? FormVersion,
    bool? HasExternalIncome,
    bool SupplementCompleted,
    decimal? AnafPrefilledNetIncome,
    PrefillCheck Prefill);

/// <summary>Ecranul anual al unui PFA (spec declarații §7 „Admin — anual”).</summary>
/// <param name="D205">Doar pentru PFA-urile cu contract de chirie de la persoane fizice (F40).</param>
public sealed record AnnualDeclarationsDto(
    Guid PfaId,
    int Year,
    AnnualDeclarationDto<D207DataModel> D207,
    AnnualDeclarationDto<D205DataModel>? D205,
    AnnualDeclarationDto<D212Dto> D212,
    int PendingLegalConfirmations);

internal static class AnnualDtos
{
    public static async Task<AnnualDeclarationsDto> BuildAsync(IApplicationDbContext db, AnnualData data, Guid pfaId, CancellationToken cancellationToken)
    {
        string period = data.Year.ToString(CultureInfo.InvariantCulture);
        var records = (await db.DeclarationVersions.AsNoTracking()
                .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Period == period &&
                            v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
                .Select(v => new { v.Declaration.Type, v.DeclarationId, v.Id, v.VersionNo, v.Status, v.Amount })
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.Type, r => new AnnualRecordDto(r.DeclarationId, r.Id, r.VersionNo, r.Status, r.Amount));
        var start = new DateOnly(data.Year, 1, 1);
        var end = new DateOnly(data.Year, 12, 31);
        int pending = await db.NonResidentPayments.AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.PaymentDate >= start && p.PaymentDate <= end)
            .Join(db.NonResidentTaxDecisions.Where(d => d.Status == NonResidentDecisionStatus.NeedsLegalConfirmation), p => p.Id, d => d.PaymentId, (p, d) => d.Id)
            .CountAsync(cancellationToken);

        AnnualDeclarationDto<T> Row<T>(DeclarationType type, T model, IReadOnlyList<string> review) =>
            new(type, data.IsReady(type), data.BlockersOf(type), review, model, records.GetValueOrDefault(type));

        D212Calculation d212 = data.D212.Model;
        return new AnnualDeclarationsDto(
            pfaId,
            data.Year,
            Row(DeclarationType.D207, data.D207.Model, data.D207.Review),
            data.D205 is { } d205 ? Row(DeclarationType.D205, d205.Model, d205.Review) : null,
            Row(
                DeclarationType.D212,
                new D212Dto(
                    d212.Model,
                    d212.Model is { } model && data.Form is { } form ? form.Map(model) : [],
                    data.Form?.FormVersion,
                    data.Answers?.HasExternalIncome,
                    data.Answers?.SupplementCompleted ?? false,
                    data.Answers?.AnafPrefilledNetIncome,
                    d212.Prefill),
                data.D212.Review),
            pending);
    }

    public static readonly Error InvalidYear = Error.Problem("Accounting.AnnualYear", "Anul nu e valid.");

    public static bool IsValidYear(int year) => year is >= 2000 and <= 2100;
}

/// <summary><c>GET /accounting/pfas/{pfaId}/annual/{year}</c></summary>
public sealed record GetAnnualDeclarationsQuery(Guid PfaId, int Year) : IQuery<AnnualDeclarationsDto>;

internal sealed class GetAnnualDeclarationsQueryHandler(IApplicationDbContext db, AnnualDeclarationService annual)
    : IQueryHandler<GetAnnualDeclarationsQuery, AnnualDeclarationsDto>
{
    public async Task<Result<AnnualDeclarationsDto>> Handle(GetAnnualDeclarationsQuery query, CancellationToken cancellationToken)
    {
        if (!AnnualDtos.IsValidYear(query.Year))
        {
            return Result.Failure<AnnualDeclarationsDto>(AnnualDtos.InvalidYear);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<AnnualDeclarationsDto>(AccountingErrors.PfaNotFound);
        }

        AnnualData data = await annual.LoadAsync(query.PfaId, query.Year, cancellationToken);
        return await AnnualDtos.BuildAsync(db, data, query.PfaId, cancellationToken);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/annual/{year}/generate</c> — doar declarațiile fără blocaje.</summary>
public sealed record GenerateAnnualDeclarationsCommand(Guid PfaId, int Year) : ICommand<AnnualDeclarationsDto>;

internal sealed class GenerateAnnualDeclarationsCommandHandler(IApplicationDbContext db, AnnualDeclarationService annual, IUserContext userContext)
    : ICommandHandler<GenerateAnnualDeclarationsCommand, AnnualDeclarationsDto>
{
    public async Task<Result<AnnualDeclarationsDto>> Handle(GenerateAnnualDeclarationsCommand command, CancellationToken cancellationToken)
    {
        if (!AnnualDtos.IsValidYear(command.Year))
        {
            return Result.Failure<AnnualDeclarationsDto>(AnnualDtos.InvalidYear);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<AnnualDeclarationsDto>(AccountingErrors.PfaNotFound);
        }

        await annual.GenerateAsync(command.PfaId, command.Year, userContext.UserId, cancellationToken);
        AnnualData data = await annual.LoadAsync(command.PfaId, command.Year, cancellationToken);
        return await AnnualDtos.BuildAsync(db, data, command.PfaId, cancellationToken);
    }
}

/// <summary>
/// <c>PUT /accounting/pfas/{pfaId}/annual/{year}/answers</c> — contabilul: răspunsul despre alte venituri
/// (primit de la PFA), formularul suplimentar verificat și netul din precompletarea ANAF (F53–F54).
/// </summary>
public sealed record SaveAnnualTaxAnswersCommand(Guid PfaId, int Year, bool? HasExternalIncome, bool SupplementCompleted, decimal? AnafPrefilledNetIncome) : ICommand;

internal sealed class SaveAnnualTaxAnswersCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<SaveAnnualTaxAnswersCommand>
{
    public async Task<Result> Handle(SaveAnnualTaxAnswersCommand command, CancellationToken cancellationToken)
    {
        if (!AnnualDtos.IsValidYear(command.Year))
        {
            return Result.Failure(AnnualDtos.InvalidYear);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure(AccountingErrors.PfaNotFound);
        }

        AnnualTaxAnswers answers = await AnnualAnswers.GetOrAddAsync(db, command.PfaId, command.Year, cancellationToken);
        var before = new { answers.HasExternalIncome, answers.SupplementCompleted, answers.AnafPrefilledNetIncome };
        if (answers.HasExternalIncome != command.HasExternalIncome)
        {
            answers.HasExternalIncome = command.HasExternalIncome;
            answers.AnsweredAtUtc = DateTime.UtcNow;
            answers.AnsweredByUserId = userContext.UserId;
        }

        answers.SupplementCompleted = command.HasExternalIncome == true && command.SupplementCompleted;
        answers.AnafPrefilledNetIncome = command.AnafPrefilledNetIncome;
        AccountingAudit.Record(db, command.PfaId, nameof(AnnualTaxAnswers), answers.Id, "SAVE", before,
            new { answers.HasExternalIncome, answers.SupplementCompleted, answers.AnafPrefilledNetIncome }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class AnnualAnswers
{
    public static async Task<AnnualTaxAnswers> GetOrAddAsync(IApplicationDbContext db, Guid pfaId, int year, CancellationToken cancellationToken)
    {
        AnnualTaxAnswers? answers = await db.AnnualTaxAnswers.SingleOrDefaultAsync(a => a.PfaRegistrationId == pfaId && a.TaxYear == year, cancellationToken);
        if (answers is null)
        {
            answers = new AnnualTaxAnswers { Id = Guid.NewGuid(), PfaRegistrationId = pfaId, TaxYear = year };
            db.AnnualTaxAnswers.Add(answers);
        }

        return answers;
    }
}

/// <summary>Ce vede PFA-ul pentru anul fiscal: întrebarea despre alte venituri și D212, fără detalii tehnice.</summary>
public sealed record ClientAnnualDto(int Year, bool? HasExternalIncome, bool SupplementCompleted, decimal? IncomeTaxDue, decimal? CasDue, decimal? CassDue, DateOnly? DueDate, DeclarationStatus? D212Status);

/// <summary><c>GET /pfa/annual/{year}</c></summary>
public sealed record GetClientAnnualQuery(int Year) : IQuery<ClientAnnualDto>;

internal sealed class GetClientAnnualQueryHandler(IApplicationDbContext db, IUserContext userContext) : IQueryHandler<GetClientAnnualQuery, ClientAnnualDto>
{
    public async Task<Result<ClientAnnualDto>> Handle(GetClientAnnualQuery query, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<ClientAnnualDto>(ClientLedger.NoPfa);
        }

        if (!AnnualDtos.IsValidYear(query.Year))
        {
            return Result.Failure<ClientAnnualDto>(AnnualDtos.InvalidYear);
        }

        AnnualTaxAnswers? answers = await db.AnnualTaxAnswers.AsNoTracking().SingleOrDefaultAsync(a => a.PfaRegistrationId == pfaId && a.TaxYear == query.Year, cancellationToken);
        string period = query.Year.ToString(CultureInfo.InvariantCulture);
        var d212 = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Type == DeclarationType.D212 && v.Declaration.Period == period &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
            .Select(v => new { v.Status, v.SnapshotJson })
            .FirstOrDefaultAsync(cancellationToken);
        D212DataModel? model = d212 is null ? null : AnnualSnapshot.Read(d212.SnapshotJson)?.D212;
        Tax.TaxRuleSet rules = await Tax.TaxRuleSet.LoadAsync(db, cancellationToken);
        DateOnly? due = rules.Find(TaxRuleTypes.Deadline, "RO", new DateOnly(query.Year, 12, 31), new Tax.TaxRuleContext(nameof(DeclarationType.D212))) is { Formula: { } formula }
            ? Tax.DeclarationDeadline.Of(formula, period)
            : null;
        return new ClientAnnualDto(query.Year, answers?.HasExternalIncome, answers?.SupplementCompleted ?? false, model?.IncomeTaxDue, model?.CasDue, model?.CassDue, due, d212?.Status);
    }
}

/// <summary>
/// <c>PUT /pfa/annual/{year}/external-income</c> — F53: „Ai avut alte venituri / contribuții / situații
/// fiscale care nu apar în RIDElance?”. Da/Nu; la „Da” contabilul completează formularul suplimentar.
/// </summary>
public sealed record AnswerExternalIncomeCommand(int Year, bool HasExternalIncome) : ICommand;

internal sealed class AnswerExternalIncomeCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<AnswerExternalIncomeCommand>
{
    public async Task<Result> Handle(AnswerExternalIncomeCommand command, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure(ClientLedger.NoPfa);
        }

        if (!AnnualDtos.IsValidYear(command.Year))
        {
            return Result.Failure(AnnualDtos.InvalidYear);
        }

        AnnualTaxAnswers answers = await AnnualAnswers.GetOrAddAsync(db, pfaId, command.Year, cancellationToken);
        bool? before = answers.HasExternalIncome;
        answers.HasExternalIncome = command.HasExternalIncome;
        answers.AnsweredAtUtc = DateTime.UtcNow;
        answers.AnsweredByUserId = userContext.UserId;
        if (!command.HasExternalIncome)
        {
            answers.SupplementCompleted = false;
        }

        AccountingAudit.Record(db, pfaId, nameof(AnnualTaxAnswers), answers.Id, "EXTERNAL_INCOME", new { hasExternalIncome = before }, new { hasExternalIncome = command.HasExternalIncome }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
