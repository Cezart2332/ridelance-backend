using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.MonthlyIncome;

public sealed record GetPfaMonthlyIncomeQuery(
    Guid PfaRegistrationId,
    int Year,
    int Month) : IQuery<PfaMonthlyIncomeResponse>;

internal sealed class GetPfaMonthlyIncomeQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetPfaMonthlyIncomeQuery, PfaMonthlyIncomeResponse>
{
    public async Task<Result<PfaMonthlyIncomeResponse>> Handle(
        GetPfaMonthlyIncomeQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        Result accessResult = await EnsureAccessAsync(pfa, cancellationToken);
        if (accessResult.IsFailure)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(accessResult.Error);
        }

        PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                i => i.PfaRegistrationId == query.PfaRegistrationId
                    && i.Year == query.Year
                    && i.Month == query.Month,
                cancellationToken);

        if (income is null)
        {
            return new PfaMonthlyIncomeResponse(
                null,
                query.PfaRegistrationId,
                query.Year,
                query.Month,
                0,
                0,
                0,
                0,
                0,
                0,
                null);
        }

        return Map(income);
    }

    private async Task<Result> EnsureAccessAsync(PfaRegistration pfa, CancellationToken cancellationToken)
    {
        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure(Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        if (caller.Role is UserRole.Admin)
        {
            return Result.Success();
        }

        if (caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId)
        {
            return Result.Success();
        }

        if (caller.Role is UserRole.Client && pfa.UserId == userContext.UserId)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Failure("Pfa.AccessDenied", "Nu ai acces la aceste date."));
    }

    internal static PfaMonthlyIncomeResponse Map(PfaMonthlyIncome income) =>
        new(
            income.Id,
            income.PfaRegistrationId,
            income.Year,
            income.Month,
            income.VenitCash,
            income.VenitCard,
            income.VenitBolt,
            income.VenitUber,
            income.TaxeEstimate,
            income.ComputeVenitTotal(),
            income.UpdatedAtUtc);
}
