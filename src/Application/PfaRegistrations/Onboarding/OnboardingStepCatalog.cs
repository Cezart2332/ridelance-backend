using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Gruparea onboardingului în 6 pași și derivarea statusului fiecărui pas din frunzele/semnalele
/// lui (secțiuni de documente + entitățile ghidate). Statusul unui pas NU se stochează — se derivă
/// mereu aici, la citire. Ordinea și deblocarea (fiecare blocare explică motivul) trăiesc
/// tot aici. Oglindește ordinea din spec „Onboarding semi-final”.
///
/// Deblocarea NU e strict liniară: fiecare pas își declară dependențele reale, așa că un pas rămas
/// în verificare nu mai blochează pașii care nu atârnă de el (poți lucra la fiscal/platforme cât
/// timp dosarul PFA e la validare).
/// </summary>
public static class OnboardingStepCatalog
{
    // Statusuri de pas (string, ca și restul răspunsului).
    private const string Locked = "Locked";
    private const string InProgress = "InProgress";
    private const string AwaitingValidation = "AwaitingValidation";
    private const string Completed = "Completed";

    /// <param name="DependsOn">
    /// Ordinele pașilor care trebuie finalizați înainte. Doar dependențe reale — mereu cu Order mai
    /// mic, ca să fie deja rezolvate când ajungem la pasul curent.
    /// </param>
    private sealed record StepDef(int Order, string Key, string Label, string Path, int[] DependsOn);

    // eligibility ──> pfa ──┬──> fiscal
    //                       ├──> arr ──> vehicle
    //                       └──> platforms
    private static readonly StepDef[] Steps =
    [
        new(0, "eligibility", "Eligibilitate", "/onboarding/eligibility", []),
        // Fără eligibilitate confirmată nu are sens să deschidem dosarul.
        new(1, "pfa", "PFA", "/onboarding/pfa", [0]),
        // Bancă, TVA și Oblio se leagă de CUI-ul PFA-ului.
        new(2, "fiscal", "Fiscal, bancă & semnături", "/onboarding/step2", [1]),
        // Dosarul ARR se depune pe PFA.
        new(3, "arr", "Autorizație transport", "/onboarding/arr", [1]),
        // Conturile de operator se deschid pe PFA.
        new(4, "platforms", "Uber & Bolt", "/onboarding/platforms", [1]),
        // Copia conformă se emite pe autorizația de transport.
        new(5, "vehicle", "Vehicul, copie conformă & ecusoane", "/onboarding/vehicle", [3]),
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

        // 2) Deblocare pe dependențe reale: un pas e Locked cât timp o dependență a lui nu e
        //    finalizată. Pașii independenți rămân deschiși chiar dacă un frate e în verificare.
        var result = new List<OnboardingStepDto>(Steps.Length);
        bool[] done = new bool[Steps.Length];

        foreach (StepDef def in Steps)
        {
            string status = own[def.Order];
            string? blockReason = null;

            // Dependențele au Order mai mic ⇒ statusul lor final e deja calculat.
            int? blocker = def.DependsOn
                .Where(order => !done[order])
                .Select(order => (int?)order)
                .FirstOrDefault();

            if (blocker is not null && status != Completed)
            {
                status = Locked;
                blockReason = $"Finalizează întâi pasul „{Steps[blocker.Value].Label}”.";
            }

            result.Add(new OnboardingStepDto(def.Order, def.Key, def.Label, status, blockReason, def.Path));
            done[def.Order] = status == Completed;
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
