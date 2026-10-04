using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Associate an actual bank payment with its D301; never create a payment from a declaration.</summary>
public sealed record AssociateD301PaymentCommand(Guid PfaId, Guid EntryId, Guid VersionId, bool ConfirmNonRecoverable, string Reason)
    : ICommand<LedgerEntryDto>;

internal sealed class AssociateD301PaymentCommandHandler(IApplicationDbContext db, IUserContext user)
    : ICommandHandler<AssociateD301PaymentCommand, LedgerEntryDto>
{
    public async Task<Result<LedgerEntryDto>> Handle(AssociateD301PaymentCommand command, CancellationToken cancellationToken)
    {
        UserRole? role = await db.Users.Where(u => u.Id == user.UserId && u.DeletedAtUtc == null)
            .Select(u => (UserRole?)u.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is not (UserRole.Admin or UserRole.Contabil) ||
            !await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId && p.User.DeletedAtUtc == null &&
                (role == UserRole.Admin || p.AssignedContabilId == user.UserId), cancellationToken))
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.EntryNotFound);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.ReasonRequired);
        }

        if (!command.ConfirmNonRecoverable)
        {
            return Invalid("Confirmă că TVA-ul este nerecuperabil și aferent comisioanelor deductibile ale activității.");
        }

        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(
            e => e.Id == command.EntryId && e.PfaRegistrationId == command.PfaId, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.EntryNotFound);
        }

        Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
        if (editable.IsFailure)
        {
            return Result.Failure<LedgerEntryDto>(editable.Error);
        }

        if (entry.ClosedPeriodFlag || entry.Source != LedgerSource.Bank || entry.BankTransactionId is null ||
            entry.SettlementGroupId is not null || entry.StornoOfEntryId is not null || entry.Amount >= 0 ||
            entry.FixedAssetReview == FixedAssetReview.FixedAsset ||
            entry.Currency != "RON" || entry.PaymentMethod != PaymentMethod.Bank || entry.PersonalAmount != 0 ||
            entry.TransactionType is not (LedgerTransactionType.Tax or LedgerTransactionType.Other or LedgerTransactionType.Expense))
        {
            return Invalid("Alege o plată bancară în lei, integral aferentă D301, dintr-o lună deschisă. O plată comună pentru mai multe taxe trebuie separată și justificată înainte de asociere.");
        }

        DeclarationVersion? version = await db.DeclarationVersions.Include(v => v.Declaration)
            .SingleOrDefaultAsync(v => v.Id == command.VersionId && v.Declaration.PfaRegistrationId == command.PfaId &&
                v.Declaration.Type == DeclarationType.D301, cancellationToken);
        if (version?.PdfDocumentId is not { } documentId || version.Amount <= 0 || version.Status == DeclarationStatus.Rejected ||
            await db.DeclarationVersions.AnyAsync(v => v.DeclarationId == version.DeclarationId && v.VersionNo > version.VersionNo, cancellationToken))
        {
            return Invalid("Alege versiunea curentă a unei D301 cu sumă de plată și document PDF, din același dosar.");
        }

        if (!await db.Documents.AnyAsync(d => d.Id == documentId && d.PfaRegistrationId == command.PfaId, cancellationToken))
        {
            return Invalid("Documentul D301 nu aparține acestui dosar.");
        }

        if (await db.PfaFiscalProfiles.AnyAsync(p => p.PfaRegistrationId == command.PfaId && p.IsVatPayer, cancellationToken))
        {
            return Invalid("Dosarul este marcat plătitor de TVA. Verifică regimul fiscal înainte de tratarea TVA-ului ca nerecuperabil.");
        }

        if (entry.SourceDocumentId is not null && entry.SourceDocumentId != documentId)
        {
            return Invalid("Plata are deja alt document justificativ. Verifică asocierea existentă înainte de a o schimba.");
        }

        List<Guid> documents = await db.DeclarationVersions
            .Where(v => v.DeclarationId == version.DeclarationId && v.PdfDocumentId != null)
            .Select(v => v.PdfDocumentId!.Value).ToListAsync(cancellationToken);
        List<decimal> amounts = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == command.PfaId && e.Id != entry.Id &&
                e.Category == DeductibilityService.NonRecoverableVatCategory && e.SourceDocumentId != null &&
                documents.Contains(e.SourceDocumentId.Value) && !e.ClosedPeriodFlag)
            .Select(e => e.Amount).ToListAsync(cancellationToken);
        decimal paid = -amounts.Sum() + Math.Abs(entry.Amount);
        decimal obligation = Math.Round(version.Amount, 0, MidpointRounding.AwayFromZero);
        if (paid > obligation)
        {
            return Invalid($"Plățile asociate ({AccountingJson.Amount(paid)} lei) depășesc D301 ({AccountingJson.Amount(obligation)} lei). Verifică taxa și eventualele sume plătite în plus.");
        }

        LedgerRules rules = await LedgerSupport.RulesAsync(db, command.PfaId, cancellationToken);
        if (!rules.Categories.Any(r => r.Category == DeductibilityService.NonRecoverableVatCategory &&
            r.DefaultDeductibility == DeductibilityType.Percent100 && r.ValidFrom <= entry.Date &&
            (r.ValidTo is null || r.ValidTo >= entry.Date)))
        {
            return Invalid("Lipsește regula pentru TVA nerecuperabil la data plății.");
        }

        if (entry.Category == DeductibilityService.NonRecoverableVatCategory && entry.SourceDocumentId == documentId &&
            entry.TransactionType == LedgerTransactionType.Expense && entry.ReconciliationStatus == ReconciliationStatus.Matched)
        {
            return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
        }

        var before = new { entry.TransactionType, entry.Category, entry.SourceDocumentId, entry.Description, entry.ReconciliationStatus };
        entry.TransactionType = LedgerTransactionType.Expense;
        entry.Category = DeductibilityService.NonRecoverableVatCategory;
        entry.SourceDocumentId = documentId;
        entry.Description = $"TVA nerecuperabil achitat la ANAF, D301 {version.Declaration.Period} v{version.VersionNo}";
        entry.ProposedClassification = null;
        entry.ReconciliationStatus = ReconciliationStatus.Matched;
        entry.Status = LedgerEntryStatus.Verified;
        entry.FixedAssetReview = FixedAssetReview.None;
        DeductibilityService.Resolve(entry, rules);
        AccountingAudit.Record(db, command.PfaId, nameof(LedgerEntry), entry.Id, "ASSOCIATE_D301_PAYMENT", before,
            new { entry.TransactionType, entry.Category, entry.SourceDocumentId, entry.Description, entry.ReconciliationStatus, command.VersionId },
            command.Reason.Trim(), user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }

    private static Result<LedgerEntryDto> Invalid(string message) =>
        Result.Failure<LedgerEntryDto>(Error.Problem("Accounting.D301Payment", message));
}
