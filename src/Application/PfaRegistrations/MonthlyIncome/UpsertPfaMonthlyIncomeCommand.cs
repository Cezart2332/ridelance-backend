using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.MonthlyIncome;

public sealed record UpsertPfaMonthlyIncomeCommand(
    Guid PfaRegistrationId,
    int Year,
    int Month,
    decimal VenitCash,
    decimal VenitCard,
    decimal VenitBolt,
    decimal VenitUber,
    decimal TaxeEstimate) : ICommand<PfaMonthlyIncomeResponse>;

internal sealed class UpsertPfaMonthlyIncomeCommandValidator : AbstractValidator<UpsertPfaMonthlyIncomeCommand>
{
    public UpsertPfaMonthlyIncomeCommandValidator()
    {
        RuleFor(c => c.Year).InclusiveBetween(2000, 2100);
        RuleFor(c => c.Month).InclusiveBetween(1, 12);
        RuleFor(c => c.VenitCash).GreaterThanOrEqualTo(0);
        RuleFor(c => c.VenitCard).GreaterThanOrEqualTo(0);
        RuleFor(c => c.VenitBolt).GreaterThanOrEqualTo(0);
        RuleFor(c => c.VenitUber).GreaterThanOrEqualTo(0);
        RuleFor(c => c.TaxeEstimate).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpsertPfaMonthlyIncomeCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpsertPfaMonthlyIncomeCommand, PfaMonthlyIncomeResponse>
{
    public async Task<Result<PfaMonthlyIncomeResponse>> Handle(
        UpsertPfaMonthlyIncomeCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        bool canEdit = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!canEdit)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a modifica veniturile."));
        }

        PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
            .SingleOrDefaultAsync(
                i => i.PfaRegistrationId == command.PfaRegistrationId
                    && i.Year == command.Year
                    && i.Month == command.Month,
                cancellationToken);

        if (income is null)
        {
            income = new PfaMonthlyIncome
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                Year = command.Year,
                Month = command.Month,
            };
            context.PfaMonthlyIncomes.Add(income);
        }

        income.VenitCash = command.VenitCash;
        income.VenitCard = command.VenitCard;
        income.VenitBolt = command.VenitBolt;
        income.VenitUber = command.VenitUber;
        income.TaxeEstimate = command.TaxeEstimate;
        income.UpdatedAtUtc = DateTime.UtcNow;
        income.UpdatedByUserId = userContext.UserId;

        await context.SaveChangesAsync(cancellationToken);

        return GetPfaMonthlyIncomeQueryHandler.Map(income);
    }
}
