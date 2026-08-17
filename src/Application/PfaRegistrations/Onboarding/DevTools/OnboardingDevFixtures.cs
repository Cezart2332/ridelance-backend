using Application.Abstractions.Data;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.DevTools;

/// <summary>
/// Datele de test cu care se umple un pas sărit (spec fix-uri §13.4).
///
/// De ce trebuie să existe: saltul lasă starea incompletă, iar pașii următori se blochează dacă
/// așteaptă date de la cei dinainte. Un salt la pasul 06 fără fixtures ar produce exact ecranul
/// gol pe care testerul voia să-l evite.
///
/// Toate valorile stau AICI, într-un singur fișier versionat — nu împrăștiate prin comenzi. CNP-ul
/// de test are cifra de control corectă, ca validarea reală să treacă peste el.
/// </summary>
public static class OnboardingDevFixtures
{
    /// <summary>CNP de test cu checksum valid (bărbat, 12.05.1990).</summary>
    public const string Cnp = "1900512350016";

    public const string FullName = "Test Onboarding";
    public const string LegalName = "PFA Test Onboarding";
    public const string Cui = "RO00000000";
    public const string County = "Cluj";
    public const string City = "Cluj-Napoca";
    public const string Street = "Strada Testelor";
    public const string Number = "1";
    public const string PostalCode = "400001";
    public const string PlateNumber = "CJ01TST";
    public const string Iban = "RO49AAAA1B31007593840000";
    public const string ArrAuthorizationNumber = "TEST-ARR-0001";

    /// <summary>Pașii mari, în ordinea reală. Saltul la unul îi completează pe toți dinaintea lui.</summary>
    public static readonly OnboardingStepKey[] Order =
    [
        OnboardingStepKey.Eligibility,
        OnboardingStepKey.Pfa,
        OnboardingStepKey.Fiscal,
        OnboardingStepKey.Arr,
        OnboardingStepKey.Platforms,
        OnboardingStepKey.Vehicle,
    ];

    /// <summary>Cheia unui pas, exact ca cea trimisă clientului.</summary>
    public static string KeyOf(OnboardingStepKey step) => step switch
    {
        OnboardingStepKey.Eligibility => "eligibility",
        OnboardingStepKey.Pfa => "pfa",
        OnboardingStepKey.Fiscal => "fiscal",
        OnboardingStepKey.Arr => "arr",
        OnboardingStepKey.Platforms => "platforms",
        OnboardingStepKey.Vehicle => "vehicle",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    public static bool TryParseKey(string? key, out OnboardingStepKey step)
    {
        foreach (OnboardingStepKey candidate in Order)
        {
            if (string.Equals(KeyOf(candidate), key, StringComparison.OrdinalIgnoreCase))
            {
                step = candidate;
                return true;
            }
        }

        step = default;
        return false;
    }

    /// <summary>
    /// Umple un pas cu fixture-ul lui, ca pașii următori să aibă tot ce le trebuie.
    ///
    /// Forțarea e deliberat aceeași ca la <c>SkipOnboardingStepCommandHandler</c>: statusurile
    /// pașilor se derivă din entități, deci un pas „completat" înseamnă entitățile lui aduse în
    /// starea din care derivarea iese `Completed`.
    /// </summary>
    public static void Apply(
        IApplicationDbContext context,
        PfaRegistration registration,
        OnboardingEligibilityProfile? eligibility,
        OnboardingStepKey step,
        DateTime nowUtc)
    {
        switch (step)
        {
            case OnboardingStepKey.Eligibility:
                ApplyEligibility(context, registration.UserId, eligibility, nowUtc);
                break;
            case OnboardingStepKey.Pfa:
                ApplyPfa(registration, nowUtc);
                break;
            case OnboardingStepKey.Fiscal:
                ApplyFiscal(context, registration, nowUtc);
                break;
            case OnboardingStepKey.Arr:
                ApplyArr(context, registration, nowUtc);
                break;
            case OnboardingStepKey.Platforms:
                ApplyPlatforms(context, registration, nowUtc);
                break;
            case OnboardingStepKey.Vehicle:
                ApplyVehicle(context, registration, nowUtc);
                break;
            default:
                break;
        }
    }

    public static OnboardingEligibilityProfile ApplyEligibility(
        IApplicationDbContext context,
        Guid userId,
        OnboardingEligibilityProfile? profile,
        DateTime nowUtc)
    {
        if (profile is null)
        {
            profile = new OnboardingEligibilityProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = nowUtc,
            };
            context.OnboardingEligibilityProfiles.Add(profile);
        }

        profile.DateOfBirth ??= DateOnly.FromDateTime(nowUtc.AddYears(-30));
        profile.CategoryBObtainedOn ??= DateOnly.FromDateTime(nowUtc.AddYears(-5));
        profile.DrivingLicenceExpiresOn ??= DateOnly.FromDateTime(nowUtc.AddYears(5));
        profile.HasDriverCertificate = true;
        profile.DriverCertificateExpiresOn ??= DateOnly.FromDateTime(nowUtc.AddYears(2));
        profile.Status = EligibilityStatus.Eligible;
        profile.UpdatedAtUtc = nowUtc;

        return profile;
    }

    private static void ApplyPfa(PfaRegistration registration, DateTime nowUtc)
    {
        registration.Status = PfaRegistrationStatus.Approved;
        registration.ReviewedAtUtc = nowUtc;
        registration.ReviewNote = null;
        registration.Cui ??= Cui;
        registration.LegalName ??= LegalName;
        registration.County ??= County;
        registration.City ??= City;
        registration.Street ??= Street;
        registration.Number ??= Number;
    }

    private static void ApplyFiscal(IApplicationDbContext context, PfaRegistration registration, DateTime nowUtc)
    {
        PfaFiscalProfile fiscal = registration.FiscalProfile ?? Add(context, registration, new PfaFiscalProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
        });

        // „Nu", nu „Da": un „Da" ar cere certificatul de TVA intracomunitar, pe care fixture-ul
        // nu-l are ca document.
        fiscal.VatAnswer = VatAnswer.No;
        fiscal.VatRegistrationKind = VatRegistrationKind.None;
        fiscal.SpecialVatCodeStatus = PfaSpecialVatCodeStatus.No;

        PfaBankAccountDeclaration bank = registration.BankAccountDeclaration ?? Add(context, registration,
            new PfaBankAccountDeclaration
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                CreatedAtUtc = nowUtc,
            });

        bank.Status = BankDeclarationStatus.Verified;
        bank.IbanMasked ??= $"{Iban[..4]}••••{Iban[^4..]}";
        bank.UpdatedAtUtc = nowUtc;

        PfaOblioAccount oblio = registration.OblioAccount ?? Add(context, registration, new PfaOblioAccount
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        });

        oblio.AccountCreationConsent = true;
        oblio.DataProcessingConsent = true;
        oblio.EInvoiceConsent = true;
        oblio.AutoInvoicingConsent = true;
        oblio.RidelanceManagementConsent = true;
        oblio.TermsAcceptedConsent = true;
        oblio.IntegrationStatus = OblioIntegrationStatus.Active;
        oblio.UpdatedAtUtc = nowUtc;

        OnboardingSignaturePacket packet = registration.SignaturePacket ?? Add(context, registration,
            new OnboardingSignaturePacket
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                CreatedAtUtc = nowUtc,
            });

        packet.Status = SignaturePacketStatus.Completed;
        packet.SubmittedForReviewAtUtc ??= nowUtc;
        packet.SignedAtUtc ??= nowUtc;
        packet.PackageName ??= "Fixture dev";
        packet.SignatureCount ??= 1;
        packet.RejectionReason = null;
        packet.UpdatedAtUtc = nowUtc;
    }

    private static void ApplyArr(IApplicationDbContext context, PfaRegistration registration, DateTime nowUtc)
    {
        ArrAuthorizationRequest arr = registration.ArrAuthorizationRequest ?? Add(context, registration,
            new ArrAuthorizationRequest
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                CreatedAtUtc = nowUtc,
            });

        arr.AgencyName ??= County;
        arr.Status = ArrAuthorizationStatus.Issued;
        arr.AuthorizationNumber ??= ArrAuthorizationNumber;
        arr.AuthorizationExpiresOn ??= DateOnly.FromDateTime(nowUtc.AddYears(5));
        arr.SubmittedAtUtc ??= nowUtc;
        arr.UpdatedAtUtc = nowUtc;
    }

    private static void ApplyPlatforms(IApplicationDbContext context, PfaRegistration registration, DateTime nowUtc)
    {
        foreach (PfaPlatformProvider provider in new[] { PfaPlatformProvider.Uber, PfaPlatformProvider.Bolt })
        {
            PfaPlatformAccount? account = registration.PlatformAccounts
                .FirstOrDefault(p => p.Provider == provider && p.Kind == PfaPlatformAccountKind.Driver);

            if (account is null)
            {
                account = new PfaPlatformAccount
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = registration.Id,
                    Provider = provider,
                    Kind = PfaPlatformAccountKind.Driver,
                };
                context.PfaPlatformAccounts.Add(account);
                registration.PlatformAccounts.Add(account);
            }

            account.IsSelectedByUser = true;
            account.OnboardingStatus = PfaPlatformOnboardingStatus.Active;
            account.UpdatedAtUtc = nowUtc;
        }
    }

    private static void ApplyVehicle(IApplicationDbContext context, PfaRegistration registration, DateTime nowUtc)
    {
        PfaVehicle? vehicle = registration.Vehicles
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault();

        if (vehicle is null)
        {
            vehicle = new PfaVehicle
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                PlateNumber = PlateNumber,
                OwnershipMode = VehicleOwnershipMode.Owned,
                Status = PfaVehicleStatus.Active,
                CreatedAtUtc = nowUtc,
            };
            context.PfaVehicles.Add(vehicle);
            registration.Vehicles.Add(vehicle);
        }

        vehicle.PlateNumber ??= PlateNumber;
        vehicle.Status = PfaVehicleStatus.Active;

        VehicleCopyRequest copy = vehicle.CopyRequest ?? new VehicleCopyRequest
        {
            Id = Guid.NewGuid(),
            PfaVehicleId = vehicle.Id,
            Years = 1,
            CreatedAtUtc = nowUtc,
        };

        if (vehicle.CopyRequest is null)
        {
            context.VehicleCopyRequests.Add(copy);
            vehicle.CopyRequest = copy;
        }

        copy.Status = VehicleCopyRequestStatus.Issued;
        copy.SubmittedAtUtc ??= nowUtc;
        copy.UpdatedAtUtc = nowUtc;
    }

    private static T Add<T>(IApplicationDbContext context, PfaRegistration registration, T entity)
        where T : class
    {
        switch (entity)
        {
            case PfaFiscalProfile fiscal:
                context.PfaFiscalProfiles.Add(fiscal);
                registration.FiscalProfile = fiscal;
                break;
            case PfaBankAccountDeclaration bank:
                context.PfaBankAccountDeclarations.Add(bank);
                registration.BankAccountDeclaration = bank;
                break;
            case PfaOblioAccount oblio:
                context.PfaOblioAccounts.Add(oblio);
                registration.OblioAccount = oblio;
                break;
            case OnboardingSignaturePacket packet:
                context.OnboardingSignaturePackets.Add(packet);
                registration.SignaturePacket = packet;
                break;
            case ArrAuthorizationRequest arr:
                context.ArrAuthorizationRequests.Add(arr);
                registration.ArrAuthorizationRequest = arr;
                break;
            default:
                throw new ArgumentException($"Fixture nemapat: {typeof(T).Name}", nameof(entity));
        }

        return entity;
    }
}
