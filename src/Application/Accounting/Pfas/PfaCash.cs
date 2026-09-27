using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Pfas;

internal static class CashErrors
{
    public static readonly Error NoteRequired = Error.Problem("Accounting.ReasonRequired", "Nota e obligatorie.");

    public static readonly Error EvidenceRequired = Error.Problem("Accounting.EvidenceRequired", "Încarcă dovada de fiscalizare înainte de activarea numerarului.");

    public static readonly Error NoPfa = Error.NotFound("Accounting.PfaNotFound", "Contul tău nu are un PFA.");

    public static Error InvalidTransition(CashRegisterStatus from, CashRegisterStatus to) => Error.Conflict(
        "Accounting.InvalidTransition",
        $"Casa de marcat nu poate trece din {AccountingJson.Serialize(from).Trim('"')} în {AccountingJson.Serialize(to).Trim('"')}.");
}

/// <summary>
/// Traseul casei de marcat (spec §3.4, F7), oglinda lui <c>cashWorkflow.ts</c>: INACTIV → ÎN
/// VERIFICARE → ACTIV; <c>ACTIVE</c> doar din <c>IN_VERIFICATION</c> și doar cu dovada de fiscalizare.
/// </summary>
internal static class CashWorkflow
{
    public static readonly IReadOnlyDictionary<CashRegisterStatus, CashRegisterStatus[]> Transitions = new Dictionary<CashRegisterStatus, CashRegisterStatus[]>
    {
        [CashRegisterStatus.NotRequiredCurrentConfiguration] = [CashRegisterStatus.Pending, CashRegisterStatus.InVerification],
        [CashRegisterStatus.Pending] = [CashRegisterStatus.InVerification, CashRegisterStatus.NotRequiredCurrentConfiguration],
        [CashRegisterStatus.InVerification] = [CashRegisterStatus.Active, CashRegisterStatus.Pending, CashRegisterStatus.NotRequiredCurrentConfiguration],
        [CashRegisterStatus.Active] = [CashRegisterStatus.NotRequiredCurrentConfiguration],
    };

    public static bool CanTransition(CashRegisterStatus from, CashRegisterStatus to) => Transitions[from].Contains(to);
}

/// <summary><c>POST /accounting/pfas/{pfaId}/cash/evidence</c> — multipart(file) → <c>{ documentId }</c>.</summary>
public sealed record UploadCashEvidenceCommand(Guid PfaId, LedgerUpload File) : ICommand<CashEvidenceUploadResult>;

internal sealed class UploadCashEvidenceCommandHandler(
    IApplicationDbContext db,
    DeclarationFiles files,
    IUserContext userContext,
    IOptions<AccountingOptions> options)
    : ICommandHandler<UploadCashEvidenceCommand, CashEvidenceUploadResult>
{
    public async Task<Result<CashEvidenceUploadResult>> Handle(UploadCashEvidenceCommand command, CancellationToken cancellationToken)
    {
        Result allowed = await LedgerUploadErrors.CheckAsync(db, command.PfaId, command.File, options.Value, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<CashEvidenceUploadResult>(allowed.Error);
        }

        Document document = await files.StoreAsync(
            command.PfaId, command.File.Content, command.File.FileName, command.File.ContentType, cancellationToken, DocumentOrigin.AccountingUpload);
        AccountingAudit.Record(db, command.PfaId, "CashEvidence", document.Id, "UPLOAD", null, new { command.File.FileName }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new CashEvidenceUploadResult(document.Id);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/cash/transition</c> — <c>{ to, note, evidenceDocumentId? }</c>.</summary>
public sealed record TransitionCashCommand(Guid PfaId, CashTransitionRequest Request) : ICommand<CashRegisterStateDto>;

internal sealed class TransitionCashCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<TransitionCashCommand, CashRegisterStateDto>
{
    public async Task<Result<CashRegisterStateDto>> Handle(TransitionCashCommand command, CancellationToken cancellationToken)
    {
        CashTransitionRequest request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Result.Failure<CashRegisterStateDto>(CashErrors.NoteRequired);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<CashRegisterStateDto>(AccountingErrors.PfaNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, string.Empty, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<CashRegisterStateDto>(writable.Error);
        }

        CashRegisterState? existing = await db.CashRegisterStates.FirstOrDefaultAsync(c => c.PfaRegistrationId == command.PfaId, cancellationToken);
        CashRegisterState cash = existing ?? new CashRegisterState { PfaRegistrationId = command.PfaId };
        CashRegisterStatus from = cash.Status;
        if (!CashWorkflow.CanTransition(from, request.To))
        {
            return Result.Failure<CashRegisterStateDto>(CashErrors.InvalidTransition(from, request.To));
        }

        bool active = request.To == CashRegisterStatus.Active;
        if (active && (request.EvidenceDocumentId is not { } evidence ||
                       !await db.Documents.AnyAsync(d => d.Id == evidence && d.PfaRegistrationId == command.PfaId, cancellationToken)))
        {
            return Result.Failure<CashRegisterStateDto>(CashErrors.EvidenceRequired);
        }

        if (existing is null)
        {
            db.CashRegisterStates.Add(cash);
        }

        var before = new { cash.Status, cash.CashEnabled, cash.ActivationDate, cash.EvidenceDocumentId };
        cash.Status = request.To;
        cash.CashRequested = request.To != CashRegisterStatus.NotRequiredCurrentConfiguration;
        cash.CashEnabled = active;
        cash.ActivationDate = active ? DateOnly.FromDateTime(DateTime.UtcNow) : null;
        cash.VerifiedByUserId = active ? userContext.UserId : null;
        if (active)
        {
            cash.EvidenceDocumentId = request.EvidenceDocumentId;
        }
        else if (request.To == CashRegisterStatus.NotRequiredCurrentConfiguration)
        {
            cash.EvidenceDocumentId = null;
        }
        cash.UpdatedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(
            db, command.PfaId, nameof(CashRegisterState), command.PfaId, $"CASH_{AccountingJson.Serialize(request.To).Trim('"')}", before,
            new { cash.Status, cash.CashEnabled, cash.ActivationDate, cash.EvidenceDocumentId }, request.Note.Trim(), userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        return (await PfaSummaries.BuildAsync(db, command.PfaId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken))!.Cash;
    }
}

/// <summary><c>GET /accounting/me/cash-preference</c> — rol PFA: răspunsul din onboarding, pasul 3.</summary>
public sealed record GetMyCashPreferenceQuery : IQuery<CashPreference?>;

internal sealed class GetMyCashPreferenceQueryHandler(IApplicationDbContext db, IUserContext userContext) : IQueryHandler<GetMyCashPreferenceQuery, CashPreference?>
{
    public async Task<Result<CashPreference?>> Handle(GetMyCashPreferenceQuery query, CancellationToken cancellationToken)
    {
        Guid? pfaId = await MyCashPreference.PfaOfAsync(db, userContext.UserId, cancellationToken);
        if (pfaId is null)
        {
            return Result.Failure<CashPreference?>(CashErrors.NoPfa);
        }

        CashRegisterState? cash = await db.CashRegisterStates.AsNoTracking().FirstOrDefaultAsync(c => c.PfaRegistrationId == pfaId, cancellationToken);
        return Result.Success(cash?.CashRequestedAnsweredAtUtc is { } answered ? new CashPreference(cash.CashRequested, answered) : null);
    }
}

/// <summary><c>PUT /accounting/me/cash-preference</c> — <c>{ cashRequested }</c>.</summary>
public sealed record SetMyCashPreferenceCommand(bool CashRequested) : ICommand<CashPreference>;

/// <summary>
/// Răspunsul PFA-ului la întrebarea despre numerar (F7). DA pune casa de marcat în <c>PENDING</c>
/// (activarea rămâne a contabilului, cu dovada de fiscalizare); NU o lasă fără cerință. O casă deja
/// în verificare sau activă nu se schimbă din onboarding.
/// </summary>
internal sealed class SetMyCashPreferenceCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<SetMyCashPreferenceCommand, CashPreference>
{
    public async Task<Result<CashPreference>> Handle(SetMyCashPreferenceCommand command, CancellationToken cancellationToken)
    {
        Guid? pfaId = await MyCashPreference.PfaOfAsync(db, userContext.UserId, cancellationToken);
        if (pfaId is not { } id)
        {
            return Result.Failure<CashPreference>(CashErrors.NoPfa);
        }

        CashRegisterState cash = await db.CashRegisterStates.FirstOrDefaultAsync(c => c.PfaRegistrationId == id, cancellationToken)
            ?? db.CashRegisterStates.Add(new CashRegisterState { PfaRegistrationId = id }).Entity;
        var before = new { cash.CashRequested, cash.Status };
        cash.CashRequested = command.CashRequested;
        cash.CashRequestedAnsweredAtUtc = DateTime.UtcNow;
        if (cash.Status is CashRegisterStatus.NotRequiredCurrentConfiguration or CashRegisterStatus.Pending)
        {
            cash.Status = command.CashRequested ? CashRegisterStatus.Pending : CashRegisterStatus.NotRequiredCurrentConfiguration;
        }

        cash.UpdatedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(db, id, nameof(CashRegisterState), id, command.CashRequested ? "CASH_PENDING" : "CASH_NOT_REQUIRED_CURRENT_CONFIGURATION",
            before, new { cash.CashRequested, cash.Status }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new CashPreference(cash.CashRequested, cash.CashRequestedAnsweredAtUtc.Value);
    }
}

internal static class MyCashPreference
{
    /// <summary>PFA-ul utilizatorului (cel mai recent, dacă are mai multe înregistrări).</summary>
    public static Task<Guid?> PfaOfAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.PfaRegistrations.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
