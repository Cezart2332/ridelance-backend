using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Gruparea onboardingului în 6 pași și derivarea statusului fiecărui pas din frunzele/semnalele
/// lui (secțiuni de documente + entitățile ghidate). Statusul unui pas NU se stochează — se derivă
/// mereu aici, la citire. Ordinea și deblocarea secvențială (fiecare blocare explică motivul) trăiesc
/// tot aici. Oglindește ordinea din spec „Onboarding semi-final”.
/// </summary>
public static class OnboardingStepCatalog
{
    // Statusuri de pas (string, ca și restul răspunsului).
    private const string Locked = "Locked";
    private const string InProgress = "InProgress";
    private const string AwaitingValidation = "AwaitingValidation";
    private const string Completed = "Completed";

    private sealed record StepDef(int Order, string Key, string Label, string Path);

    private static readonly StepDef[] Steps =
    [
        new(0, "eligibility", "Eligibilitate", "/onboarding/eligibility"),
        new(1, "pfa", "PFA", "/onboarding/pfa"),
        new(2, "fiscal", "Fiscal, bancă & semnături", "/onboarding/step2"),
        new(3, "arr", "Autorizație transport", "/onboarding/arr"),
        new(4, "platforms", "Uber & Bolt", "/onboarding/platforms"),
        new(5, "vehicle", "Vehicul, copie conformă & ecusoane", "/onboarding/vehicle"),
    ];

    /// <summary>Toți cei 6 pași sunt finalizați — condiția reală de înrolare.</summary>
    public static bool AllCompleted(IReadOnlyList<OnboardingStepDto> steps) =>
        steps.Count > 0 && steps.All(s => s.Status == Completed);

    public static List<OnboardingStepDto> BuildSteps(
        PfaRegistration? registration,
        OnboardingSectionStatus pfaStatus,
        OnboardingEligibilityProfile? eligibility)
    {
        // 1) Statusul „propriu” al fiecărui pas, înainte de gating.
        string[] own =
        [
            EligibilityStatusOf(eligibility),
            PfaStatusOf(pfaStatus),
            FiscalStatusOf(registration),
            ArrStatusOf(registration),
            PlatformsStatusOf(registration),
            VehicleStatusOf(registration),
        ];

        // 2) Deblocare secvențială: un pas e Locked cât timp cel anterior nu e Completed.
        var result = new List<OnboardingStepDto>(Steps.Length);
        bool previousDone = true;

        foreach (StepDef def in Steps)
        {
            string status = own[def.Order];
            string? blockReason = null;

            if (!previousDone && status != Completed)
            {
                status = Locked;
                blockReason = def.Order == 0
                    ? null
                    : $"Finalizează întâi pasul „{Steps[def.Order - 1].Label}”.";
            }

            result.Add(new OnboardingStepDto(def.Order, def.Key, def.Label, status, blockReason, def.Path));
            previousDone = status == Completed;
        }

        return result;
    }

    private static string EligibilityStatusOf(OnboardingEligibilityProfile? profile) => profile?.Status switch
    {
        EligibilityStatus.Eligible => Completed,
        _ => InProgress,
    };

    private static string PfaStatusOf(OnboardingSectionStatus pfaStatus) => pfaStatus switch
    {
        OnboardingSectionStatus.Validated => Completed,
        OnboardingSectionStatus.AwaitingValidation => AwaitingValidation,
        _ => InProgress,
    };

    private static string FiscalStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return InProgress;
        }

        bool bankOk = r.BankAccountDeclaration?.Status == BankDeclarationStatus.Verified;
        bool oblioOk = r.OblioAccount?.AllConsentsAccepted == true;
        bool vatOk = r.FiscalProfile?.VatAnswer is not null and not VatAnswer.Unknown;

        return bankOk && oblioOk && vatOk ? Completed : InProgress;
    }

    private static string ArrStatusOf(PfaRegistration? r)
    {
        ArrAuthorizationStatus? status = r?.ArrAuthorizationRequest?.Status;
        return status == ArrAuthorizationStatus.Issued ? Completed : InProgress;
    }

    private static string PlatformsStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return InProgress;
        }

        var selected = r.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .ToList();

        if (selected.Count == 0)
        {
            return InProgress;
        }

        return selected.All(p => p.OnboardingStatus == PfaPlatformOnboardingStatus.Active)
            ? Completed
            : InProgress;
    }

    private static string VehicleStatusOf(PfaRegistration? r)
    {
        PfaVehicle? vehicle = r?.Vehicles
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault();

        if (vehicle?.CopyRequest?.Status == VehicleCopyRequestStatus.Issued)
        {
            return Completed;
        }

        return InProgress;
    }
}
