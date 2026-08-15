using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Expenses.GetByPfa;

public sealed record GetDeductibleExpensesByPfaQuery(
    Guid PfaRegistrationId,
    int? Year,
    int? Month) : IQuery<List<DeductibleExpenseResponse>>;

internal sealed class GetDeductibleExpensesByPfaQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetDeductibleExpensesByPfaQuery, List<DeductibleExpenseResponse>>
{
    public async Task<Result<List<DeductibleExpenseResponse>>> Handle(
        GetDeductibleExpensesByPfaQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<List<DeductibleExpenseResponse>>(ExpenseErrors.PfaNotFound);
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<List<DeductibleExpenseResponse>>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        bool canView = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId
            || caller.Role is UserRole.Client && pfa.UserId == userContext.UserId;

        if (!canView)
        {
            return Result.Failure<List<DeductibleExpenseResponse>>(ExpenseErrors.AccessDenied);
        }

        IQueryable<Domain.Expenses.DeductibleExpense> expensesQuery = context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == query.PfaRegistrationId);

        if (query.Year.HasValue)
        {
            expensesQuery = expensesQuery.Where(e => e.Year == query.Year.Value);
        }

        if (query.Month.HasValue)
        {
            expensesQuery = expensesQuery.Where(e => e.Month == query.Month.Value);
        }

        List<DeductibleExpenseResponse> items = await expensesQuery
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new DeductibleExpenseResponse(
                e.Id,
                e.DocumentId,
                e.UserId,
                e.PfaRegistrationId,
                e.CatalogCategory,
                e.ItemName,
                e.DeductibleLabel,
                e.AmountRon,
                e.Year,
                e.Month,
                e.Document.Status.ToString(),
                e.Document.OriginalFileName,
                e.Document.FileSize,
                e.Document.UploadedAtUtc,
                e.CreatedAtUtc,
                e.CreatedByUserId,
                e.ExpenseDate,
                e.SupplierName,
                e.VatAmount,
                e.Currency,
                e.DocumentTypeLabel,
                e.Source.ToString(),
                e.Status.ToString()))
            .ToListAsync(cancellationToken);

        return items;
    }
}
