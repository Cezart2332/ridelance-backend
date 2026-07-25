using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Settings;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — clientul declară vehiculul (mod de deținere, date, sau amână adăugarea).</summary>
public sealed record SubmitVehicleCommand(
    Guid UserId,
    VehicleOwnershipMode OwnershipMode,
    bool AddLater,
    string? PlateNumber,
    string? Vin,
    string? Make,
    string? Model,
    int? FirstRegistrationYear,
    Guid? MarketplaceCarId) : ICommand<VehicleStateResponse>;

internal sealed class SubmitVehicleCommandHandler(
    IApplicationDbContext context,
    IAppSettings appSettings)
    : ICommandHandler<SubmitVehicleCommand, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        SubmitVehicleCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .Include(r => r.Vehicles).ThenInclude(v => v.Badges)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;
        PfaVehicle? vehicle = VehicleShared.PrimaryVehicle(registration);

        if (vehicle is null)
        {
            vehicle = new PfaVehicle
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                CreatedAtUtc = nowUtc,
            };
            context.PfaVehicles.Add(vehicle);
            registration.Vehicles.Add(vehicle);
        }

        vehicle.OwnershipMode = command.AddLater ? VehicleOwnershipMode.AddedLater : command.OwnershipMode;
        vehicle.AddLater = command.AddLater;
        // Datele mașinii vin din documente (OCR) — nu le ștergem când userul salvează doar
        // răspunsurile (mod de deținere). Valorile explicite le suprascriu pe cele citite.
        vehicle.PlateNumber = Coalesce(command.PlateNumber, vehicle.PlateNumber);
        vehicle.Vin = Coalesce(command.Vin, vehicle.Vin);
        vehicle.Make = Coalesce(command.Make, vehicle.Make);
        vehicle.Model = Coalesce(command.Model, vehicle.Model);
        vehicle.FirstRegistrationYear = command.FirstRegistrationYear ?? vehicle.FirstRegistrationYear;
        vehicle.MarketplaceCarId = command.MarketplaceCarId ?? vehicle.MarketplaceCarId;
        vehicle.Status = command.AddLater ? PfaVehicleStatus.Draft : PfaVehicleStatus.DocumentsPending;
        vehicle.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        long copyFee = await appSettings.GetAsync(
            VehicleShared.CopyFeePerYearSettingKey, VehicleShared.DefaultCopyFeePerYearBani, cancellationToken);
        long badgeFee = await appSettings.GetAsync(
            VehicleShared.BadgeFeePerSetSettingKey, VehicleShared.DefaultBadgeFeePerSetBani, cancellationToken);

        return Result.Success(VehicleShared.ToResponse(vehicle, copyFee, badgeFee));
    }

    private static string? Coalesce(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming.Trim();
}
