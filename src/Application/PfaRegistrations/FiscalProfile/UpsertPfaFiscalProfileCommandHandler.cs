using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal sealed class UpsertPfaFiscalProfileCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpsertPfaFiscalProfileCommand, PfaFiscalProfileResponse>
{
    public async Task<Result<PfaFiscalProfileResponse>> Handle(
        UpsertPfaFiscalProfileCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> access = await PfaAccess.EnsureCanManageAsync(
            context,
            userContext,
            command.PfaRegistrationId,
            cancellationToken);

        if (access.IsFailure)
        {
            return Result.Failure<PfaFiscalProfileResponse>(access.Error);
        }

        if (!TryParse(command.SpecialVatCodeStatus, out PfaSpecialVatCodeStatus vatStatus) ||
            !TryParse(command.UberStatus, out PfaPlatformStatus uberStatus) ||
            !TryParse(command.BoltStatus, out PfaPlatformStatus boltStatus) ||
            !TryParse(command.OtherPlatformsStatus, out PfaTriStateStatus otherPlatformsStatus) ||
            !TryParse(command.CashRevenueStatus, out PfaTriStateStatus cashRevenueStatus) ||
            !TryParse(command.CashRegisterStatus, out PfaCashRegisterStatus cashRegisterStatus) ||
            !TryParse(command.VehicleUsageType, out PfaVehicleUsageType vehicleUsageType))
        {
            return Result.Failure<PfaFiscalProfileResponse>(
                Error.Failure("PfaFiscalProfile.InvalidValue", "One or more fiscal profile values are invalid."));
        }

        PfaFiscalProfile? profile = await context.PfaFiscalProfiles
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == command.PfaRegistrationId, cancellationToken);

        if (profile is null)
        {
            profile = new PfaFiscalProfile
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.PfaFiscalProfiles.Add(profile);
        }

        profile.TaxationSystem = PfaTaxationSystem.RealSystem;
        profile.IsVatPayer = false;
        profile.HasEmployees = false;
        profile.AccountingRegime = PfaAccountingRegime.SingleEntry;
        profile.SpecialVatCodeStatus = vatStatus;
        profile.SpecialVatCodeObtainedAtUtc = command.SpecialVatCodeObtainedAtUtc.HasValue
            ? DateTime.SpecifyKind(command.SpecialVatCodeObtainedAtUtc.Value.Date, DateTimeKind.Utc)
            : null;
        profile.SpecialVatCodeDocumentId = command.SpecialVatCodeDocumentId;
        profile.UberStatus = uberStatus;
        profile.BoltStatus = boltStatus;
        profile.OtherPlatformsStatus = otherPlatformsStatus;
        profile.CashRevenueStatus = cashRevenueStatus;
        profile.CashRegisterStatus = cashRegisterStatus;
        profile.VehicleUsageType = vehicleUsageType;
        profile.VehicleSupportingDocumentLabel = string.IsNullOrWhiteSpace(command.VehicleSupportingDocumentLabel)
            ? null
            : command.VehicleSupportingDocumentLabel.Trim();
        profile.VehicleSupportingDocumentId = command.VehicleSupportingDocumentId;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedByUserId = userContext.UserId;

        await context.SaveChangesAsync(cancellationToken);

        return PfaFiscalProfileMapper.MapProfile(profile);
    }

    private static bool TryParse<T>(string value, out T parsed)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed);
}
