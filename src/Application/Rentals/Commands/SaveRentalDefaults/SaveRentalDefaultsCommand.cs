using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Commands.SaveRentalDefaults;

public sealed record SaveRentalDefaultsCommand(RentalDefaultsDto Defaults) : ICommand<RentalDefaultsDto>;

internal sealed class SaveRentalDefaultsCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<SaveRentalDefaultsCommand, RentalDefaultsDto>
{
    public async Task<Result<RentalDefaultsDto>> Handle(
        SaveRentalDefaultsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RentalDefaultsDto input = command.Defaults;

        FleetRentalDefaults? defaults = await context.FleetRentalDefaults
            .FirstOrDefaultAsync(d => d.OwnerUserId == userContext.UserId, cancellationToken);

        if (defaults is null)
        {
            defaults = new FleetRentalDefaults { Id = Guid.NewGuid(), OwnerUserId = userContext.UserId };
            context.FleetRentalDefaults.Add(defaults);
        }

        defaults.WeeklyRentBani = input.WeeklyRentBani;
        defaults.DepositBani = input.DepositBani;
        defaults.MinPeriodDays = input.MinPeriodDays;
        defaults.HasKmLimit = input.HasKmLimit;
        // Limita fără „cu limită" ar fi rămas ca o cifră fantomă în formular.
        defaults.MileageLimit = input.HasKmLimit ? input.MileageLimit : null;
        defaults.ExtraKmCostBani = input.ExtraKmCostBani;
        defaults.FuelRule = input.FuelRule?.Trim();
        defaults.DefaultConditions = input.DefaultConditions?.Trim();
        defaults.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(input with { MileageLimit = defaults.MileageLimit });
    }
}
