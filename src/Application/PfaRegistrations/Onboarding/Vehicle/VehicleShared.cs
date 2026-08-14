using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

internal static class VehicleShared
{
    /// <summary>Taxa copie conformă (bani/an); implicit 100 lei = 10000 bani.</summary>
    public const string CopyFeePerYearSettingKey = "fees.copieconforma.peryear.bani";
    public const long DefaultCopyFeePerYearBani = 10_000;

    /// <summary>Taxa ecusoane (bani/set); implicit 8 lei = 800 bani.</summary>
    public const string BadgeFeePerSetSettingKey = "fees.ecusoane.perset.bani";
    public const long DefaultBadgeFeePerSetBani = 800;

    public static readonly Error NoRegistration = Error.Problem(
        "Onboarding.Vehicle.NoRegistration",
        "Nu există un dosar PFA pentru utilizatorul curent.");

    /// <summary>
    /// Numele solicitantului apare pe cererea de copie conformă. Ca la ARR: fără nume nu se
    /// generează dosarul, nu se completează cu un fallback.
    /// </summary>
    public static readonly Error ApplicantNameMissing = Error.Problem(
        "Onboarding.Vehicle.ApplicantNameMissing",
        "Completează numele și prenumele înainte de a genera dosarul — apar pe cererea depusă.");

    public static readonly Error VehicleNotFound = Error.NotFound(
        "Onboarding.Vehicle.NotFound",
        "Nu există un vehicul declarat pentru acest dosar.");

    public static readonly Error CopyRequestNotFound = Error.NotFound(
        "Onboarding.Vehicle.CopyRequestNotFound",
        "Nu există o cerere de copie conformă pentru acest vehicul.");

    public static readonly Error InvalidPeriod = Error.Problem(
        "Onboarding.Vehicle.InvalidPeriod",
        $"Perioada copiei conforme trebuie să fie între {CopyConformaRules.MinYears} și {CopyConformaRules.MaxYears} ani.");

    public static PfaVehicle? PrimaryVehicle(PfaRegistration registration) =>
        registration.Vehicles
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault();

    public static VehicleStateResponse ToResponse(
        PfaVehicle? vehicle,
        long copyFeePerYearBani,
        long badgeFeePerSetBani)
    {
        if (vehicle is null)
        {
            return new VehicleStateResponse(
                null,
                VehicleOwnershipMode.Owned.ToString(),
                false,
                null, null, null, null, null, null,
                PfaVehicleStatus.Draft.ToString(),
                null,
                [],
                copyFeePerYearBani,
                badgeFeePerSetBani,
                CopyConformaRules.MaxYears);
        }

        CopyRequestDto? copy = vehicle.CopyRequest is null ? null : ToCopyDto(vehicle.CopyRequest);

        var badges = vehicle.Badges
            .OrderBy(b => b.Provider)
            .Select(ToBadgeDto)
            .ToList();

        return new VehicleStateResponse(
            vehicle.Id,
            vehicle.OwnershipMode.ToString(),
            vehicle.AddLater,
            vehicle.PlateNumber,
            vehicle.Vin,
            vehicle.Make,
            vehicle.Model,
            vehicle.FirstRegistrationYear,
            vehicle.MarketplaceCarId,
            vehicle.Status.ToString(),
            copy,
            badges,
            copyFeePerYearBani,
            badgeFeePerSetBani,
            CopyConformaRules.MaxYears);
    }

    private static CopyRequestDto ToCopyDto(VehicleCopyRequest c) => new(
        c.Years,
        c.FeePerYearSnapshotBani,
        c.TotalFeeSnapshotBani,
        c.Status.ToString(),
        c.DossierDocumentId is not null,
        c.DossierDocumentId,
        c.DossierGeneratedAtUtc,
        c.SubmittedAtUtc,
        c.CopyConformaDocumentId,
        c.CopyConformaNumber,
        c.IssuedOn,
        c.ExpiresOn,
        c.AdminNote);

    private static VehicleBadgeDto ToBadgeDto(VehicleBadge b) => new(
        b.Provider.ToString(),
        b.SetCount,
        b.FeePerSetSnapshotBani,
        b.TotalFeeSnapshotBani,
        b.Status.ToString(),
        b.BadgeDocumentId);
}
