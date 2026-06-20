using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.FiscalProfile;

internal static class PfaFiscalProfileMapper
{
    public static PfaFiscalProfileResponse MapProfile(PfaFiscalProfile profile) =>
        new(
            profile.Id,
            profile.PfaRegistrationId,
            profile.TaxationSystem.ToString(),
            profile.IsVatPayer,
            profile.HasEmployees,
            profile.AccountingRegime.ToString(),
            profile.SpecialVatCodeStatus.ToString(),
            profile.SpecialVatCodeObtainedAtUtc,
            profile.SpecialVatCodeDocumentId,
            profile.UberStatus.ToString(),
            profile.BoltStatus.ToString(),
            profile.OtherPlatformsStatus.ToString(),
            profile.CashRevenueStatus.ToString(),
            profile.CashRegisterStatus.ToString(),
            profile.VehicleUsageType.ToString(),
            profile.VehicleSupportingDocumentLabel,
            profile.VehicleSupportingDocumentId,
            profile.UpdatedAtUtc);

    public static PfaFiscalProfileResponse DefaultProfile(Guid pfaRegistrationId) =>
        MapProfile(new PfaFiscalProfile
        {
            Id = Guid.Empty,
            PfaRegistrationId = pfaRegistrationId,
            TaxationSystem = PfaTaxationSystem.RealSystem,
            IsVatPayer = false,
            HasEmployees = false,
            AccountingRegime = PfaAccountingRegime.SingleEntry,
            SpecialVatCodeStatus = PfaSpecialVatCodeStatus.ToVerify,
            UberStatus = PfaPlatformStatus.Inactive,
            BoltStatus = PfaPlatformStatus.Inactive,
            OtherPlatformsStatus = PfaTriStateStatus.No,
            CashRevenueStatus = PfaTriStateStatus.ToVerify,
            CashRegisterStatus = PfaCashRegisterStatus.ToVerify,
            VehicleUsageType = PfaVehicleUsageType.OwnCar
        }) with { Id = null, UpdatedAtUtc = null };

    public static PfaPlatformAccountResponse MapAccount(PfaPlatformAccount account) =>
        new(
            account.Id,
            account.PfaRegistrationId,
            account.Provider.ToString(),
            account.Kind.ToString(),
            account.Email,
            account.Phone,
            account.FullName,
            !string.IsNullOrWhiteSpace(account.PasswordProtected),
            account.Status.ToString(),
            account.ConfiguredAtUtc,
            account.UpdatedAtUtc);

    public static PfaFleetConsentResponse MapConsent(PfaFleetConsent consent) =>
        new(
            consent.Id,
            consent.PfaRegistrationId,
            consent.FleetAccountsAccepted,
            consent.FleetAccountsAcceptedAtUtc,
            consent.BoltApiAccepted,
            consent.BoltApiAcceptedAtUtc,
            consent.ConsentTextVersion);

    public static PfaFleetConsentResponse DefaultConsent(Guid pfaRegistrationId) =>
        new(null, pfaRegistrationId, false, null, false, null, "2026-06");
}
