using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Expenses.Create;
using Application.Expenses.Ocr;
using Domain.Accounting;
using Domain.Documents;
using Domain.Expenses;
using Domain.PfaRegistrations;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Expenses.Update;

/// <summary>
/// Pasul de confirmare din fluxul cerut de spec §7.2: după ce OCR-ul a precompletat, omul
/// verifică, corectează ce e greșit și confirmă. Abia atunci cheltuiala intră în profit.
/// </summary>
public sealed record UpdateDeductibleExpenseCommand(
    Guid PfaRegistrationId,
    Guid ExpenseId,
    string CatalogCategory,
    string ItemName,
    string DeductibleLabel,
    decimal? AmountRon,
    int Year,
    int Month,
    DateOnly? ExpenseDate,
    string? SupplierName,
    decimal? VatAmount,
    string? DocumentTypeLabel,
    bool Confirm,
    string? PaymentMethod = null,
    DateOnly? PaymentDate = null,
    Guid? LedgerEntryId = null,
    string? AccountingCategory = null,
    decimal PersonalAmount = 0,
    string? DocumentNumber = null,
    string? Reason = null,
    bool ApproveDocument = false) : ICommand<DeductibleExpenseResponse>;

internal sealed class UpdateDeductibleExpenseCommandValidator : AbstractValidator<UpdateDeductibleExpenseCommand>
{
    public UpdateDeductibleExpenseCommandValidator()
    {
        RuleFor(c => c.PfaRegistrationId).NotEmpty();
        RuleFor(c => c.ExpenseId).NotEmpty();
        RuleFor(c => c.CatalogCategory).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ItemName).NotEmpty().MaximumLength(500);
        RuleFor(c => c.DeductibleLabel).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Year).InclusiveBetween(2000, 2100);
        RuleFor(c => c.Month).InclusiveBetween(1, 12);
        RuleFor(c => c.AmountRon).GreaterThanOrEqualTo(0).When(c => c.AmountRon.HasValue);
        RuleFor(c => c.VatAmount).GreaterThanOrEqualTo(0).When(c => c.VatAmount.HasValue);
        RuleFor(c => c.SupplierName).MaximumLength(300);
        RuleFor(c => c.DocumentTypeLabel).MaximumLength(100);
        RuleFor(c => c.DocumentNumber).MaximumLength(64);
        RuleFor(c => c.AccountingCategory).MaximumLength(64);
        RuleFor(c => c.PersonalAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Reason).MaximumLength(1024);

        RuleFor(c => c)
            .Must(c => MoneyParser.IsVatPlausible(c.AmountRon, c.VatAmount))
            .WithMessage("TVA-ul nu poate depăși suma totală.");

        // Confirmarea are efect asupra profitului; fără sumă nu are ce confirma.
        RuleFor(c => c.AmountRon)
            .NotNull()
            .When(c => c.Confirm)
            .WithMessage("O cheltuială confirmată are nevoie de sumă.");
    }
}

internal sealed class UpdateDeductibleExpenseCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ExpenseAccountingService accounting)
    : ICommandHandler<UpdateDeductibleExpenseCommand, DeductibleExpenseResponse>
{
    public async Task<Result<DeductibleExpenseResponse>> Handle(
        UpdateDeductibleExpenseCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(ExpenseErrors.PfaNotFound);
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        bool canManage = caller is not null &&
            (caller.Role is UserRole.Admin
                || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId
                || caller.Role is UserRole.Client && pfa.UserId == userContext.UserId);

        if (!canManage)
        {
            return Result.Failure<DeductibleExpenseResponse>(ExpenseErrors.AccessDenied);
        }

        if (command.ApproveDocument && caller?.Role is not (UserRole.Admin or UserRole.Contabil))
        {
            return Result.Failure<DeductibleExpenseResponse>(ExpenseErrors.AccessDenied);
        }

        DeductibleExpense? expense = await context.DeductibleExpenses
            .SingleOrDefaultAsync(
                e => e.Id == command.ExpenseId && e.PfaRegistrationId == command.PfaRegistrationId,
                cancellationToken);

        if (expense is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.NotFound("Expense.NotFound", "Cheltuiala nu a fost găsită."));
        }

        // Guard existing payments before changing their source data, including closed periods.
        Guid? linked = await context.ExpenseDocuments.Where(d => d.DocumentId == expense.DocumentId && d.PfaRegistrationId == pfa.Id)
            .Select(d => d.LedgerEntryId).SingleOrDefaultAsync(cancellationToken);
        if (linked is {} linkedId)
        {
            LedgerEntry payment = await context.LedgerEntries.SingleAsync(e => e.Id == linkedId && e.PfaRegistrationId == pfa.Id, cancellationToken);
            Result editable = await Application.Accounting.Ledger.UpdateLedgerEntryCommandHandler.EnsureEditableAsync(context, payment, cancellationToken);
            if (editable.IsFailure)
            {
                return Result.Failure<DeductibleExpenseResponse>(editable.Error);
            }

            if (!command.Confirm || command.PaymentMethod == null)
            {
                return Result.Failure<DeductibleExpenseResponse>(Error.Problem("Expense.PaymentExists", "Cheltuiala are o plată în registre. Corectează documentul împreună cu plata."));
            }
        }
        expense.CatalogCategory = command.CatalogCategory.Trim();
        expense.ItemName = command.ItemName.Trim();
        expense.DeductibleLabel = command.DeductibleLabel.Trim();
        expense.AmountRon = command.AmountRon;
        expense.Year = command.ExpenseDate?.Year ?? command.Year;
        expense.Month = command.ExpenseDate?.Month ?? command.Month;
        expense.ExpenseDate = command.ExpenseDate;
        expense.SupplierName = string.IsNullOrWhiteSpace(command.SupplierName) ? null : command.SupplierName.Trim();
        expense.VatAmount = command.VatAmount;
        expense.DocumentTypeLabel = string.IsNullOrWhiteSpace(command.DocumentTypeLabel)
            ? null
            : command.DocumentTypeLabel.Trim();
        expense.Status = command.Confirm ? ExpenseStatus.Confirmed : ExpenseStatus.Draft;
        expense.UpdatedAtUtc = DateTime.UtcNow;

        if (command.Confirm)
        {
            Result saved = await accounting.SavePaymentAsync(expense, command.PaymentMethod, command.PaymentDate,
                command.LedgerEntryId, command.AccountingCategory, command.PersonalAmount, command.DocumentNumber, command.Reason, cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<DeductibleExpenseResponse>(saved.Error);
            }
        }

        if (command.ApproveDocument)
        {
            Document reviewed = await context.Documents.SingleAsync(d => d.Id == expense.DocumentId && d.PfaRegistrationId == pfa.Id, cancellationToken);
            reviewed.Status = DocumentStatus.Verified;
            reviewed.ReviewNote = null;
        }

        await context.SaveChangesAsync(cancellationToken);

        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == expense.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.Failure("Expense.DocumentMissing", "Documentul cheltuielii nu mai există."));
        }

        Domain.Accounting.LedgerEntry? savedPayment = await context.LedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PfaRegistrationId == pfa.Id && e.SourceDocumentId == expense.DocumentId, cancellationToken);
        return CreateDeductibleExpenseCommandHandler.Map(expense, document) with
        {
            LedgerEntryId = savedPayment?.Id,
            DeductibleAmount = savedPayment?.ReconciliationStatus is Domain.Accounting.ReconciliationStatus.Matched or Domain.Accounting.ReconciliationStatus.Partial ? savedPayment.DeductibleAmount : null,
            PaymentMethod = savedPayment?.PaymentMethod.ToString(),
            PaymentDate = savedPayment?.Date,
        };
    }
}
