using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.Upload;
using Application.Expenses.Ocr;
using Domain.Documents;
using Domain.Expenses;
using Domain.PfaRegistrations;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Expenses.Create;

public sealed record CreateDeductibleExpenseCommand(
    Guid PfaRegistrationId,
    string CatalogCategory,
    string ItemName,
    string DeductibleLabel,
    decimal? AmountRon,
    int Year,
    int Month,
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSize,
    DateOnly? ExpenseDate = null,
    string? SupplierName = null,
    decimal? VatAmount = null,
    string? DocumentTypeLabel = null) : ICommand<DeductibleExpenseResponse>;

internal sealed class CreateDeductibleExpenseCommandValidator : AbstractValidator<CreateDeductibleExpenseCommand>
{
    public CreateDeductibleExpenseCommandValidator()
    {
        RuleFor(c => c.PfaRegistrationId).NotEmpty();
        RuleFor(c => c.CatalogCategory).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ItemName).NotEmpty().MaximumLength(500);
        RuleFor(c => c.DeductibleLabel).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Year).InclusiveBetween(2000, 2100);
        RuleFor(c => c.Month).InclusiveBetween(1, 12);
        RuleFor(c => c.AmountRon).GreaterThanOrEqualTo(0).When(c => c.AmountRon.HasValue);
        RuleFor(c => c.VatAmount).GreaterThanOrEqualTo(0).When(c => c.VatAmount.HasValue);
        RuleFor(c => c.SupplierName).MaximumLength(300);
        RuleFor(c => c.DocumentTypeLabel).MaximumLength(100);
        // TVA-ul nu poate depăși totalul; o extragere care spune altfel a citit greșit ceva.
        RuleFor(c => c)
            .Must(c => MoneyParser.IsVatPlausible(c.AmountRon, c.VatAmount))
            .WithMessage("TVA-ul nu poate depăși suma totală.");
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.FileSize).GreaterThan(0);
    }
}

internal sealed class CreateDeductibleExpenseCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ICommandHandler<UploadDocumentCommand, Guid> uploadHandler)
    : ICommandHandler<CreateDeductibleExpenseCommand, DeductibleExpenseResponse>
{
    public async Task<Result<DeductibleExpenseResponse>> Handle(
        CreateDeductibleExpenseCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(ExpenseErrors.PfaNotFound);
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        bool canManage = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId
            || caller.Role is UserRole.Client && pfa.UserId == userContext.UserId;

        if (!canManage)
        {
            return Result.Failure<DeductibleExpenseResponse>(ExpenseErrors.AccessDenied);
        }

        string displayFileName = BuildDisplayFileName(
            command.ItemName,
            command.AmountRon,
            command.FileName);

        var uploadCommand = new UploadDocumentCommand(
            pfa.UserId,
            command.PfaRegistrationId,
            DocumentCategory.Cheltuiala,
            displayFileName,
            command.ContentType,
            command.FileStream,
            command.FileSize,
            null);

        Result<Guid> uploadResult = await uploadHandler.Handle(uploadCommand, cancellationToken);

        if (uploadResult.IsFailure)
        {
            return Result.Failure<DeductibleExpenseResponse>(uploadResult.Error);
        }

        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == uploadResult.Value, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DeductibleExpenseResponse>(
                Error.Failure("Expense.UploadFailed", "Documentul nu a putut fi salvat."));
        }

        var expense = new DeductibleExpense
        {
            Id = Guid.NewGuid(),
            UserId = pfa.UserId,
            PfaRegistrationId = command.PfaRegistrationId,
            DocumentId = document.Id,
            CatalogCategory = command.CatalogCategory.Trim(),
            ItemName = command.ItemName.Trim(),
            DeductibleLabel = command.DeductibleLabel.Trim(),
            AmountRon = command.AmountRon,
            Year = command.Year,
            Month = command.Month,
            ExpenseDate = command.ExpenseDate,
            SupplierName = string.IsNullOrWhiteSpace(command.SupplierName) ? null : command.SupplierName.Trim(),
            VatAmount = command.VatAmount,
            DocumentTypeLabel = string.IsNullOrWhiteSpace(command.DocumentTypeLabel) ? null : command.DocumentTypeLabel.Trim(),
            // Fără sumă nu există cheltuială de calculat: rândul rămâne ciornă până când omul
            // completează ce n-a putut citi OCR-ul.
            Status = command.AmountRon.HasValue ? ExpenseStatus.Confirmed : ExpenseStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userContext.UserId,
        };

        context.DeductibleExpenses.Add(expense);
        await context.SaveChangesAsync(cancellationToken);

        return Map(expense, document);
    }

    private static string BuildDisplayFileName(string itemName, decimal? amountRon, string originalFileName)
    {
        string ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".pdf";
        }

        string amountPart = amountRon.HasValue && amountRon.Value > 0
            ? $" - {amountRon.Value:N2} lei"
            : string.Empty;

        string safe = $"{itemName.Trim()}{amountPart}"
            .Replace('/', '-')
            .Replace('\\', '-');

        if (safe.Length > 100)
        {
            safe = safe[..100];
        }

        return $"{safe}{ext}";
    }

    internal static DeductibleExpenseResponse Map(DeductibleExpense expense, Document document) =>
        new(
            expense.Id,
            expense.DocumentId,
            expense.UserId,
            expense.PfaRegistrationId,
            expense.CatalogCategory,
            expense.ItemName,
            expense.DeductibleLabel,
            expense.AmountRon,
            expense.Year,
            expense.Month,
            document.Status.ToString(),
            document.OriginalFileName,
            document.FileSize,
            document.UploadedAtUtc,
            expense.CreatedAtUtc,
            expense.CreatedByUserId,
            expense.ExpenseDate,
            expense.SupplierName,
            expense.VatAmount,
            expense.Currency,
            expense.DocumentTypeLabel,
            expense.Source.ToString(),
            expense.Status.ToString());
}
