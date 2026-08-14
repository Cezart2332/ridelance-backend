using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.TestSkip;

/// <summary>
/// DOAR PENTRU TESTARE — de șters. Avansează onboardingul cu un pas (din cei 6) forțând starea
/// entităților ghidate, FĂRĂ documente. Fiecare apel finalizează primul pas neîncheiat, ca testerul
/// să ajungă la înrolare fără să încarce nimic. Înrolarea reală se produce tot prin poarta unică
/// (<see cref="OnboardingProgress.TryMarkCompleted"/>) când toți cei 6 pași sunt Completed.
/// </summary>
internal sealed class SkipOnboardingStepCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SkipOnboardingStepCommand>
{
    private static readonly Error AlreadyComplete = Error.Problem(
        "Onboarding.TestSkip.AlreadyComplete",
        "Toți pașii sunt deja finalizați.");

    public async Task<Result> Handle(SkipOnboardingStepCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.OnboardingSections)
            .Include(r => r.FiscalProfile)
            .Include(r => r.BankAccountDeclaration)
            .Include(r => r.OblioAccount)
            // Fără pachetul de semnături și cererea de înființare, BuildSteps de mai jos derivă
            // pașii „fiscal" și „pfa" din navigații null: skipul ar recalcula mereu același pas.
            .Include(r => r.SignaturePacket)
            .Include(r => r.CompanyFormationRequest)
            .Include(r => r.ArrAuthorizationRequest)
            .Include(r => r.PlatformAccounts)
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;

        OnboardingEligibilityProfile? eligibility = await context.OnboardingEligibilityProfiles
            .FirstOrDefaultAsync(e => e.UserId == command.UserId, cancellationToken);

        // Aceeași derivare ca în OnboardingStateBuilder — fără dosar, pasul PFA e „în lucru",
        // ca userul să vadă formularul, nu ecranul „în validare".
        OnboardingSectionStatus pfaStatus = registration switch
        {
            null => OnboardingSectionStatus.InProgress,
            { Status: PfaRegistrationStatus.Approved } => OnboardingSectionStatus.Validated,
            { Status: PfaRegistrationStatus.Rejected } => OnboardingSectionStatus.Rejected,
            _ => OnboardingSectionStatus.AwaitingValidation,
        };

        List<OnboardingStepDto> steps = OnboardingStepCatalog.BuildSteps(registration, pfaStatus, eligibility);
        OnboardingStepDto? next = steps.FirstOrDefault(s => s.Status != "Completed");

        if (next is null)
        {
            return Result.Failure(AlreadyComplete);
        }

        if (next.Key == "eligibility")
        {
            // Pasul 0 nu are nevoie de dosar — nu creăm unul, ca pasul PFA să rămână completabil.
            ForceEligibility(eligibility, command.UserId, now);
        }
        else
        {
            // Restul pașilor atârnă de dosar: dacă lipsește, îl creăm direct aprobat.
            registration ??= CreateApprovedRegistration(command.UserId, now);

            switch (next.Key)
            {
                case "pfa": ForcePfa(registration, now); break;
                case "fiscal": ForceFiscal(registration, now); break;
                case "arr": ForceArr(registration, now); break;
                case "platforms": ForcePlatforms(registration, now); break;
                case "vehicle": ForceVehicle(registration, now); break;
                default: break;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private void ForceEligibility(OnboardingEligibilityProfile? profile, Guid userId, DateTime now)
    {
        if (profile is null)
        {
            profile = new OnboardingEligibilityProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAtUtc = now,
            };
            context.OnboardingEligibilityProfiles.Add(profile);
        }

        profile.DateOfBirth ??= DateOnly.FromDateTime(now.AddYears(-30));
        profile.CategoryBObtainedOn ??= DateOnly.FromDateTime(now.AddYears(-5));
        profile.HasDriverCertificate = true;
        profile.Status = EligibilityStatus.Eligible;
        profile.UpdatedAtUtc = now;
    }

    private PfaRegistration CreateApprovedRegistration(Guid userId, DateTime now)
    {
        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RegistrationType = RegistrationType.AmPfa,
            PfaSource = PfaSource.Existing,
            Status = PfaRegistrationStatus.Approved,
            FullName = "Test Skip",
            Cui = "RO00000000",
            LegalName = "PFA Test Skip",
            CreatedAtUtc = now,
            ReviewedAtUtc = now,
        };
        context.PfaRegistrations.Add(registration);
        return registration;
    }

    private static void ForcePfa(PfaRegistration registration, DateTime now)
    {
        registration.Status = PfaRegistrationStatus.Approved;
        registration.ReviewedAtUtc = now;
        registration.ReviewNote = null;
    }

    private void ForceFiscal(PfaRegistration registration, DateTime now)
    {
        PfaFiscalProfile fiscal = registration.FiscalProfile ?? AddFiscal(registration);
        // „Nu”, nu „Da”: un „Da” ar cere certificatul de TVA intracomunitar, pe care skip-ul nu-l are.
        fiscal.VatAnswer = VatAnswer.No;
        fiscal.VatRegistrationKind = VatRegistrationKind.None;
        fiscal.SpecialVatCodeStatus = PfaSpecialVatCodeStatus.No;

        PfaBankAccountDeclaration bank = registration.BankAccountDeclaration ?? AddBank(registration, now);
        bank.Status = BankDeclarationStatus.Verified;
        bank.IbanMasked ??= "RO49••••1234";
        bank.UpdatedAtUtc = now;

        PfaOblioAccount oblio = registration.OblioAccount ?? AddOblio(registration, now);
        oblio.AccountCreationConsent = true;
        oblio.DataProcessingConsent = true;
        oblio.EInvoiceConsent = true;
        oblio.AutoInvoicingConsent = true;
        oblio.RidelanceManagementConsent = true;
        oblio.TermsAcceptedConsent = true;
        oblio.IntegrationStatus = OblioIntegrationStatus.Active;
        oblio.UpdatedAtUtc = now;

        // Pasul se închide doar cu pachetul de semnături finalizat (vezi FiscalStatusOf). Partea
        // asta o face adminul în realitate, deci skipul trebuie să o forțeze explicit — altfel
        // pasul rămâne InProgress și fiecare apel îl alege din nou.
        OnboardingSignaturePacket packet = registration.SignaturePacket ?? AddSignaturePacket(registration, now);
        packet.Status = SignaturePacketStatus.Completed;
        packet.SubmittedForReviewAtUtc ??= now;
        packet.SignedAtUtc ??= now;
        packet.PackageName ??= "Test Skip";
        packet.SignatureCount ??= 1;
        packet.RejectionReason = null;
        packet.UpdatedAtUtc = now;
    }

    private void ForceArr(PfaRegistration registration, DateTime now)
    {
        ArrAuthorizationRequest arr = registration.ArrAuthorizationRequest ?? AddArr(registration, now);
        arr.Status = ArrAuthorizationStatus.Issued;
        arr.AuthorizationNumber ??= "TEST-ARR-0001";
        arr.UpdatedAtUtc = now;
    }

    private void ForcePlatforms(PfaRegistration registration, DateTime now)
    {
        PfaPlatformAccount? account = registration.PlatformAccounts
            .FirstOrDefault(p => p.Kind == PfaPlatformAccountKind.Driver);

        if (account is null)
        {
            account = new PfaPlatformAccount
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                Provider = PfaPlatformProvider.Uber,
                Kind = PfaPlatformAccountKind.Driver,
            };
            context.PfaPlatformAccounts.Add(account);
            registration.PlatformAccounts.Add(account);
        }

        account.IsSelectedByUser = true;
        account.OnboardingStatus = PfaPlatformOnboardingStatus.Active;
        account.UpdatedAtUtc = now;
    }

    private void ForceVehicle(PfaRegistration registration, DateTime now)
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
                PlateNumber = "B00TEST",
                Status = PfaVehicleStatus.Active,
                CreatedAtUtc = now,
            };
            context.PfaVehicles.Add(vehicle);
            registration.Vehicles.Add(vehicle);
        }

        VehicleCopyRequest copy = vehicle.CopyRequest ?? new VehicleCopyRequest
        {
            Id = Guid.NewGuid(),
            PfaVehicleId = vehicle.Id,
            Years = 1,
            CreatedAtUtc = now,
        };

        if (vehicle.CopyRequest is null)
        {
            context.VehicleCopyRequests.Add(copy);
            vehicle.CopyRequest = copy;
        }

        copy.Status = VehicleCopyRequestStatus.Issued;
        copy.UpdatedAtUtc = now;
    }

    private PfaFiscalProfile AddFiscal(PfaRegistration registration)
    {
        var fiscal = new PfaFiscalProfile { Id = Guid.NewGuid(), PfaRegistrationId = registration.Id };
        context.PfaFiscalProfiles.Add(fiscal);
        registration.FiscalProfile = fiscal;
        return fiscal;
    }

    private PfaBankAccountDeclaration AddBank(PfaRegistration registration, DateTime now)
    {
        var bank = new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = now,
        };
        context.PfaBankAccountDeclarations.Add(bank);
        registration.BankAccountDeclaration = bank;
        return bank;
    }

    private PfaOblioAccount AddOblio(PfaRegistration registration, DateTime now)
    {
        var oblio = new PfaOblioAccount
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = now,
        };
        context.PfaOblioAccounts.Add(oblio);
        registration.OblioAccount = oblio;
        return oblio;
    }

    private OnboardingSignaturePacket AddSignaturePacket(PfaRegistration registration, DateTime now)
    {
        var packet = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = now,
        };
        context.OnboardingSignaturePackets.Add(packet);
        registration.SignaturePacket = packet;
        return packet;
    }

    private ArrAuthorizationRequest AddArr(PfaRegistration registration, DateTime now)
    {
        var arr = new ArrAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = now,
        };
        context.ArrAuthorizationRequests.Add(arr);
        registration.ArrAuthorizationRequest = arr;
        return arr;
    }
}
