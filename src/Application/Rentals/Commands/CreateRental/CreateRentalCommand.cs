using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Commands.CreateRental;

public sealed record CreateRentalCommand(
    Guid CarId,
    string TenantName,
    string TenantType,
    string? TenantFiscalCode,
    string? TenantPhone,
    string? TenantEmail,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    long WeeklyRentBani,
    long DepositBani,
    bool HasKmLimit,
    long ExtraKmCostBani,
    string? FuelRule,
    int? StartMileage,
    string? Accessories,
    string? Notes) : ICommand<Guid>;

internal sealed class CreateRentalCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<CreateRentalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRentalCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TenantName))
        {
            return Result.Failure<Guid>(
                Error.Problem("Rental.TenantRequired", "Numele chiriașului este obligatoriu."));
        }

        if (command.EndAtUtc <= command.StartAtUtc)
        {
            return Result.Failure<Guid>(
                Error.Problem("Rental.InvalidPeriod", "Data de predare trebuie să fie după cea de preluare."));
        }

        Car? car = await context.Cars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (car.PostedByUserId != userContext.UserId)
        {
            return Result.Failure<Guid>(Error.Problem("Rental.Forbidden", "Mașina nu îți aparține."));
        }

        // O mașină nu poate fi la doi chiriași în același timp. Verificarea e pe suprapunere de
        // intervale, nu pe „există vreo închiriere activă": o rezervare viitoare care se
        // suprapune e la fel de imposibilă ca una curentă.
        bool overlaps = await context.Rentals
            .AsNoTracking()
            .AnyAsync(
                r => r.CarId == command.CarId
                    && r.ClosedAtUtc == null
                    && r.StartAtUtc < command.EndAtUtc
                    && command.StartAtUtc < r.EndAtUtc,
                cancellationToken);

        if (overlaps)
        {
            return Result.Failure<Guid>(Error.Problem(
                "Rental.Overlap",
                "Mașina are deja o închiriere în perioada aleasă."));
        }

        if (!Enum.TryParse(command.TenantType, out TenantType tenantType))
        {
            tenantType = TenantType.Individual;
        }

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            CarId = car.Id,
            OwnerUserId = userContext.UserId,
            TenantName = command.TenantName.Trim(),
            TenantType = tenantType,
            TenantFiscalCode = command.TenantFiscalCode?.Trim(),
            TenantPhone = command.TenantPhone?.Trim(),
            TenantEmail = command.TenantEmail?.Trim(),
            StartAtUtc = command.StartAtUtc,
            EndAtUtc = command.EndAtUtc,
            WeeklyRentBani = command.WeeklyRentBani,
            DepositBani = command.DepositBani,
            HasKmLimit = command.HasKmLimit,
            ExtraKmCostBani = command.ExtraKmCostBani,
            FuelRule = command.FuelRule?.Trim(),
            StartMileage = command.StartMileage,
            Accessories = command.Accessories?.Trim(),
            Notes = command.Notes?.Trim(),
        };

        context.Rentals.Add(rental);
        await context.SaveChangesAsync(cancellationToken);

        return rental.Id;
    }
}
