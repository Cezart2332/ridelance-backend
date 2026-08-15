using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Expenses.Create;
using Application.Expenses.Ocr;
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
    bool Confirm) : ICommand<DeductibleExpenseResponse>;

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
    IUserContext userContext)
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

        DeductibleExpense? expense = await context.DeductibleExpenses
            .SingleOrDefaultAsync(
                e => e.Id == command.ExpenseId && e.PfaRegistrationId == command.PfaRegistrationId,
                cancellationToken);

        if (expense is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.NotFound("Expense.NotFound", "Cheltuiala nu a fost găsită."));
        }

        expense.CatalogCategory = command.CatalogCategory.Trim();
        expense.ItemName = command.ItemName.Trim();
        expense.DeductibleLabel = command.DeductibleLabel.Trim();
        expense.AmountRon = command.AmountRon;
        expense.Year = command.Year;
        expense.Month = command.Month;
        expense.ExpenseDate = command.ExpenseDate;
        expense.SupplierName = string.IsNullOrWhiteSpace(command.SupplierName) ? null : command.SupplierName.Trim();
        expense.VatAmount = command.VatAmount;
        expense.DocumentTypeLabel = string.IsNullOrWhiteSpace(command.DocumentTypeLabel)
            ? null
            : command.DocumentTypeLabel.Trim();
        expense.Status = command.Confirm ? ExpenseStatus.Confirmed : ExpenseStatus.Draft;
        expense.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == expense.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.Failure("Expense.DocumentMissing", "Documentul cheltuielii nu mai există."));
        }

        return CreateDeductibleExpenseCommandHandler.Map(expense, document);
    }
}
