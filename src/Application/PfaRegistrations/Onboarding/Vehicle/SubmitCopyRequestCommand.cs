using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Settings;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Setul de ecusoane solicitat pentru o platformă.</summary>
public sealed record BadgeSelection(PfaPlatformProvider Provider, int SetCount);

/// <summary>
/// Pasul 5 — clientul solicită copia conformă (perioada) și ecusoanele (per platformă).
/// Taxele se snapshot-uiesc din <c>AppSetting</c> la momentul cererii.
/// </summary>
public sealed record SubmitCopyRequestCommand(
    Guid UserId,
    int Years,
    IReadOnlyList<BadgeSelection> Badges) : ICommand<VehicleStateResponse>;

internal sealed class SubmitCopyRequestCommandHandler(
    IApplicationDbContext context,
    IAppSettings appSettings)
    : ICommandHandler<SubmitCopyRequestCommand, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        SubmitCopyRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!CopyConformaRules.IsValidPeriod(command.Years))
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.InvalidPeriod);
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

        PfaVehicle? vehicle = VehicleShared.PrimaryVehicle(registration);
        if (vehicle is null)
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.VehicleNotFound);
        }

        DateTime nowUtc = DateTime.UtcNow;

        long copyFeePerYear = await appSettings.GetAsync(
            VehicleShared.CopyFeePerYearSettingKey, VehicleShared.DefaultCopyFeePerYearBani, cancellationToken);
        long badgeFeePerSet = await appSettings.GetAsync(
            VehicleShared.BadgeFeePerSetSettingKey, VehicleShared.DefaultBadgeFeePerSetBani, cancellationToken);

        // Copia conformă — dacă dosarul nu e încă generat, actualizăm perioada și re-snapshotăm taxa.
        VehicleCopyRequest copy = vehicle.CopyRequest ?? new VehicleCopyRequest
        {
            Id = Guid.NewGuid(),
            PfaVehicleId = vehicle.Id,
            CreatedAtUtc = nowUtc,
        };

        if (vehicle.CopyRequest is null)
        {
            context.VehicleCopyRequests.Add(copy);
            vehicle.CopyRequest = copy;
        }

        if (copy.Status is VehicleCopyRequestStatus.Draft or VehicleCopyRequestStatus.DossierGenerated)
        {
            copy.Years = command.Years;
            copy.FeePerYearSnapshotBani = copyFeePerYear;
            copy.TotalFeeSnapshotBani = CopyConformaRules.ComputeCopyTotalBani(copyFeePerYear, command.Years);
            copy.UpdatedAtUtc = nowUtc;
        }

        // Ecusoane — un rând per platformă selectată; seturile la 0 se șterg.
        foreach (BadgeSelection selection in command.Badges)
        {
            VehicleBadge? badge = vehicle.Badges.FirstOrDefault(b => b.Provider == selection.Provider);

            if (selection.SetCount <= 0)
            {
                if (badge is not null && badge.Status == VehicleBadgeStatus.Requested)
                {
                    context.VehicleBadges.Remove(badge);
                    vehicle.Badges.Remove(badge);
                }

                continue;
            }

            if (badge is null)
            {
                badge = new VehicleBadge
                {
                    Id = Guid.NewGuid(),
                    PfaVehicleId = vehicle.Id,
                    Provider = selection.Provider,
                    CreatedAtUtc = nowUtc,
                };
                context.VehicleBadges.Add(badge);
                vehicle.Badges.Add(badge);
            }

            if (badge.Status == VehicleBadgeStatus.Requested)
            {
                badge.SetCount = selection.SetCount;
                badge.FeePerSetSnapshotBani = badgeFeePerSet;
                badge.TotalFeeSnapshotBani = CopyConformaRules.ComputeBadgesTotalBani(badgeFeePerSet, selection.SetCount);
                badge.UpdatedAtUtc = nowUtc;
            }
        }

        vehicle.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(VehicleShared.ToResponse(vehicle, copyFeePerYear, badgeFeePerSet));
    }
}
