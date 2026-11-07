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
            account.Status.ToString(),
            account.ConfiguredAtUtc,
            account.UpdatedAtUtc,
            account.DriverEmail,
            account.DriverPhone,
            account.DriverFullName);

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

    /// <summary>
    /// Onboardingul de azi nu mai are bifele de permisiuni: cererea „ARR &amp; Cont Flotă” trimisă
    /// (sau un cont de flotă deja activ) e acordul pentru conturile de flotă, iar pentru Bolt și
    /// pentru integrarea API, când Bolt e printre platformele alese ori integrarea există.
    /// Fără asta, cardul arăta „Neacceptată” unui PFA cu flota deja funcțională.
    /// </summary>
    public static PfaFleetConsentResponse WithOnboarding(
        PfaFleetConsentResponse consent,
        IReadOnlyCollection<PfaPlatformAccount> accounts,
        DateTime? arrFleetSubmittedAtUtc,
        bool boltIntegrationExists)
    {
        DateTime? activeSince = accounts
            .Where(a => a.OnboardingStatus == PfaPlatformOnboardingStatus.Active)
            .Select(a => a.UpdatedAtUtc)
            .Min();
        bool anyActive = accounts.Any(a => a.OnboardingStatus == PfaPlatformOnboardingStatus.Active);
        DateTime? acceptedAt = arrFleetSubmittedAtUtc ?? activeSince;
        bool fleetAccepted = consent.FleetAccountsAccepted || arrFleetSubmittedAtUtc is not null || anyActive;

        bool boltUsed = accounts.Any(a => a.Provider == PfaPlatformProvider.Bolt && a.IsSelectedByUser &&
            a.OnboardingStatus is not (PfaPlatformOnboardingStatus.NotStarted or PfaPlatformOnboardingStatus.Skipped));
        bool boltApiAccepted = consent.BoltApiAccepted || boltIntegrationExists || fleetAccepted && boltUsed;

        return consent with
        {
            FleetAccountsAccepted = fleetAccepted,
            FleetAccountsAcceptedAtUtc = consent.FleetAccountsAcceptedAtUtc ?? (fleetAccepted ? acceptedAt : null),
            BoltApiAccepted = boltApiAccepted,
            BoltApiAcceptedAtUtc = consent.BoltApiAcceptedAtUtc ?? (boltApiAccepted ? acceptedAt : null),
        };
    }

    /// <summary>
    /// QA 13: numerarul are o singură sursă, starea casei de marcat din contabilitate. Setările fiscale o
    /// citesc de acolo: venit cash = răspunsul „da” la numerar, casa = activă.
    /// </summary>
    public static PfaFiscalProfileResponse WithCash(PfaFiscalProfileResponse response, Domain.Accounting.CashRegisterState? cash)
    {
        if (cash is null)
        {
            return response;
        }

        string register = nameof(PfaCashRegisterStatus.No);
        if (cash.Status == Domain.Accounting.CashRegisterStatus.Active)
        {
            register = nameof(PfaCashRegisterStatus.Yes);
        }
        else if (cash.CashRequested)
        {
            register = nameof(PfaCashRegisterStatus.ToVerify);
        }

        return response with
        {
            CashRevenueStatus = cash.CashRequested ? nameof(PfaTriStateStatus.Yes) : nameof(PfaTriStateStatus.No),
            CashRegisterStatus = register,
        };
    }
}
