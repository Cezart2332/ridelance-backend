using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Settings;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — starea vehiculului, copiei conforme și ecusoanelor pentru userul curent.</summary>
public sealed record GetVehicleStateQuery(Guid UserId) : IQuery<VehicleStateResponse>;

internal sealed class GetVehicleStateQueryHandler(
    IApplicationDbContext context,
    IAppSettings appSettings)
    : IQueryHandler<GetVehicleStateQuery, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        GetVehicleStateQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .Include(r => r.Vehicles).ThenInclude(v => v.Badges)
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        long copyFee = await appSettings.GetAsync(
            VehicleShared.CopyFeePerYearSettingKey, VehicleShared.DefaultCopyFeePerYearBani, cancellationToken);
        long badgeFee = await appSettings.GetAsync(
            VehicleShared.BadgeFeePerSetSettingKey, VehicleShared.DefaultBadgeFeePerSetBani, cancellationToken);

        PfaVehicle? vehicle = registration is null ? null : VehicleShared.PrimaryVehicle(registration);

        return Result.Success(VehicleShared.ToResponse(vehicle, copyFee, badgeFee));
    }
}
