using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Queries.GetRentalDefaults;

public sealed record GetRentalDefaultsQuery : IQuery<RentalDefaultsDto>;

internal sealed class GetRentalDefaultsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetRentalDefaultsQuery, RentalDefaultsDto>
{
    public async Task<Result<RentalDefaultsDto>> Handle(
        GetRentalDefaultsQuery query,
        CancellationToken cancellationToken)
    {
        FleetRentalDefaults? defaults = await context.FleetRentalDefaults
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.OwnerUserId == userContext.UserId, cancellationToken);

        // Fără rând înseamnă „încă nu s-a setat nimic", nu o eroare: formularul rămâne gol.
        return defaults is null
            ? Result.Success(new RentalDefaultsDto(null, null, null, false, null, null, null, null))
            : Result.Success(new RentalDefaultsDto(
                defaults.WeeklyRentBani,
                defaults.DepositBani,
                defaults.MinPeriodDays,
                defaults.HasKmLimit,
                defaults.MileageLimit,
                defaults.ExtraKmCostBani,
                defaults.FuelRule,
                defaults.DefaultConditions));
    }
}
