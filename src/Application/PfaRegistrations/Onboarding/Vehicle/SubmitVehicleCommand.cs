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
    IAppSettings appSettings,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitVehicleCommand, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        SubmitVehicleCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Vehicle, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<VehicleStateResponse>(guard.Error);
        }

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

        // „Adaug mașina mai târziu" nu mai există: fără vehicul nu există copie conformă, deci
        // nici înrolare completă. Opțiunea a fost scoasă din UI, iar aici se ignoră — altfel ar
        // rămâne o portiță prin care un client vechi (sau un apel direct) sare peste pas.
        // Vezi specul de fix-uri §11.1.
        vehicle.OwnershipMode = command.OwnershipMode == VehicleOwnershipMode.AddedLater
            ? VehicleOwnershipMode.Owned
            : command.OwnershipMode;
        vehicle.AddLater = false;
        // Datele mașinii vin din documente (OCR) — nu le ștergem când userul salvează doar
        // răspunsurile (mod de deținere). Valorile explicite le suprascriu pe cele citite.
        vehicle.PlateNumber = Coalesce(command.PlateNumber, vehicle.PlateNumber);
        vehicle.Vin = Coalesce(command.Vin, vehicle.Vin);
        vehicle.Make = Coalesce(command.Make, vehicle.Make);
        vehicle.Model = Coalesce(command.Model, vehicle.Model);
        vehicle.FirstRegistrationYear = command.FirstRegistrationYear ?? vehicle.FirstRegistrationYear;
        vehicle.MarketplaceCarId = command.MarketplaceCarId ?? vehicle.MarketplaceCarId;
        vehicle.Status = PfaVehicleStatus.DocumentsPending;
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
