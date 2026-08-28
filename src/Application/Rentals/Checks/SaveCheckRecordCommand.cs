using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Checks;

/// <param name="DepositWithheldBani">Ce se reține din garanție. Cere motiv.</param>
public sealed record SaveCheckRecordCommand(
    Guid RentalId,
    CheckKind Kind,
    DateTime OccurredAtUtc,
    int Mileage,
    string? FuelLevel,
    IReadOnlyList<string>? Accessories,
    string? Notes,
    long? DepositReturnedBani,
    long? DepositWithheldBani,
    string? WithholdingReason,
    long? ExtraMileageChargeBani,
    long? OtherChargesBani) : ICommand<Guid>;

internal sealed class SaveCheckRecordCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<SaveCheckRecordCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveCheckRecordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rental? rental = await context.Rentals
            .Include(r => r.Tenant)
            .FirstOrDefaultAsync(
                r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId,
                cancellationToken);

        if (rental is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        // O sumă reținută fără motiv scris e o dispută care așteaptă să se întâmple.
        if (command.Kind == CheckKind.CheckOut
            && command.DepositWithheldBani > 0
            && string.IsNullOrWhiteSpace(command.WithholdingReason))
        {
            return Result.Failure<Guid>(Error.Problem(
                "Check.WithholdingReasonRequired",
                "Scrie motivul reținerii din garanție."));
        }

        if (command.Kind == CheckKind.CheckOut)
        {
            CheckRecord? checkIn = await context.CheckRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.RentalId == rental.Id && c.Kind == CheckKind.CheckIn, cancellationToken);

            if (checkIn is null)
            {
                return Result.Failure<Guid>(Error.Problem(
                    "Check.NoCheckIn",
                    "Nu există predare pentru închirierea asta. Primirea vine după predare."));
            }

            if (command.Mileage < checkIn.Mileage)
            {
                return Result.Failure<Guid>(Error.Problem(
                    "Check.MileageWentBackwards",
                    $"Kilometrajul la primire ({command.Mileage}) e sub cel de la predare ({checkIn.Mileage})."));
            }
        }

        CheckRecord? record = await context.CheckRecords
            .Include(c => c.Photos)
            .FirstOrDefaultAsync(c => c.RentalId == rental.Id && c.Kind == command.Kind, cancellationToken);

        bool isNew = record is null;

        record ??= new CheckRecord
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            Kind = command.Kind,
        };

        record.OccurredAtUtc = command.OccurredAtUtc;
        record.Mileage = command.Mileage;
        record.FuelLevel = command.FuelLevel?.Trim();
        record.Accessories = command.Accessories?.ToList() ?? [];
        record.Notes = command.Notes?.Trim();
        record.UpdatedAtUtc = DateTime.UtcNow;

        if (command.Kind == CheckKind.CheckOut)
        {
            record.DepositReturnedBani = command.DepositReturnedBani;
            record.DepositWithheldBani = command.DepositWithheldBani;
            record.WithholdingReason = command.WithholdingReason?.Trim();
            record.ExtraMileageChargeBani = command.ExtraMileageChargeBani;
            record.OtherChargesBani = command.OtherChargesBani;
        }

        if (isNew)
        {
            context.CheckRecords.Add(record);
        }

        // Kilometrajul mașinii vine de la primire, nu de la predare: la predare mașina pleacă, la
        // primire se știe cât a mers. De aici rezultă și calculul până la următoarea revizie.
        if (command.Kind == CheckKind.CheckOut)
        {
            Car? car = await context.Cars.FirstOrDefaultAsync(c => c.Id == rental.CarId, cancellationToken);
            if (car is not null)
            {
                car.Mileage = command.Mileage;
                car.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        VehicleTimeline.Record(
            context,
            rental.CarId,
            command.Kind == CheckKind.CheckIn ? VehicleEventType.CheckIn : VehicleEventType.CheckOut,
            command.Kind == CheckKind.CheckIn
                ? $"Predare către {rental.Tenant.Name} · {command.Mileage:N0} km"
                : $"Primire de la {rental.Tenant.Name} · {command.Mileage:N0} km",
            rental.Id,
            command.OccurredAtUtc);

        await context.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
