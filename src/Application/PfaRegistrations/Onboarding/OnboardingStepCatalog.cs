using Application.PfaRegistrations.Onboarding.Platforms;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Gruparea onboardingului în 6 pași și derivarea statusului fiecărui pas din frunzele/semnalele
/// lui (secțiuni de documente + entitățile ghidate). Statusul unui pas NU se stochează — se derivă
/// mereu aici, la citire. Ordinea și deblocarea (fiecare blocare explică motivul) trăiesc tot aici.
///
/// Deblocarea e STRICT LINIARĂ: pasul N se deschide doar când N-1 e finalizat, deci la orice moment
/// există exact un pas activ. Anterior fiecare pas își declara dependențele reale (un DAG), ceea ce
/// deschidea simultan „fiscal”, „arr” și „platforms” imediat ce dosarul PFA era gata. Un singur pas
/// activ e o cerință de produs, nu o consecință a modelului de date — de aceea lanțul e explicit.
/// </summary>
public static class OnboardingStepCatalog
{
    // Statusuri de pas — vocabularul istoric, păstrat pentru consumatorii existenți.
    private const string StatusLocked = "Locked";
    private const string StatusInProgress = "InProgress";
    private const string StatusAwaitingValidation = "AwaitingValidation";
    private const string StatusCompleted = "Completed";

    /// <summary>
    /// Vocabularul nou, mai fin, din specul v3. Trăiește lângă <c>Status</c>, nu în locul lui:
    /// frontendul migrează pe el treptat. Diferența față de <c>Status</c> e că separă „pasul e al
    /// tău, dar n-ai început” (<c>available</c>) de „ești în mijlocul lui” (<c>in_progress</c>) și
    /// scoate la suprafață respingerea, care până acum se deducea în frontend.
    /// </summary>
    public static class States
    {
        public const string Locked = "locked";
        public const string Available = "available";
        public const string InProgress = "in_progress";
        public const string PendingAdmin = "pending_admin";
        public const string Completed = "completed";
        public const string Rejected = "rejected";
    }

    /// <summary>Cine face tranziția finală a pasului. Un pas „admin” nu poate fi închis de șofer.</summary>
    public static class Owners
    {
        public const string User = "user";
        public const string Admin = "admin";
    }

    private sealed record StepDef(
        OnboardingStepKey Key,
        string WireKey,
        string Label,
        string Path,
        string OwnedBy);

    // eligibility ──> pfa ──> fiscal ──> arr ──> platforms ──> vehicle
    private static readonly StepDef[] Steps =
    [
        new(OnboardingStepKey.Eligibility, "eligibility", "Eligibilitate", "/onboarding/eligibility", Owners.User),
        // Aprobarea dosarului PFA e a adminului, indiferent de ramură.
        new(OnboardingStepKey.Pfa, "pfa", "PFA", "/onboarding/pfa", Owners.Admin),
        // Bancă, TVA și Oblio se leagă de CUI-ul PFA-ului, dar pachetul de semnături îl alocă
        // adminul — deci pasul nu poate fi închis de șofer (RL-02).
        new(OnboardingStepKey.Fiscal, "fiscal", "Fiscal, bancă & semnături", "/onboarding/step2", Owners.Admin),
        // Autorizația o emite adminul, după ce șoferul depune dosarul.
        new(OnboardingStepKey.Arr, "arr", "Autorizație transport", "/onboarding/arr", Owners.Admin),
        // Conturile de operator se activează manual din admin.
        new(OnboardingStepKey.Platforms, "platforms", "Uber Fleet & Bolt Fleet", "/onboarding/platforms", Owners.Admin),
        // Copia conformă se emite pe autorizația de transport.
        new(OnboardingStepKey.Vehicle, "vehicle", "Vehicul, copie conformă & ecusoane", "/onboarding/vehicle",
            Owners.Admin),
    ];

    /// <summary>Toți cei 6 pași sunt finalizați — condiția reală de înrolare.</summary>
    public static bool AllCompleted(IReadOnlyList<OnboardingStepDto> steps) =>
        steps.Count > 0 && steps.All(s => s.Status == StatusCompleted);

    /// <summary>
    /// Cheia pasului la care e oprit șoferul acum: primul nefinalizat. <c>null</c> când totul e gata.
    /// Fluxul fiind liniar, ăsta e și singurul pas pe care se poate scrie.
    /// </summary>
    public static string? CurrentStepKey(IReadOnlyList<OnboardingStepDto> steps) =>
        steps.FirstOrDefault(s => s.Status != StatusCompleted)?.Key;

    /// <summary>
    /// Poate userul să scrie pe pasul cerut? Funcție pură, ca regula să fie testabilă fără bază de
    /// date și să nu se rescrie ușor diferit în fiecare handler.
    ///
    /// Un pas respins rămâne scriptibil intenționat — altfel respingerea ar fi o fundătură din care
    /// șoferul nu mai poate ieși singur.
    /// </summary>
    public static bool IsWritableByUser(IReadOnlyList<OnboardingStepDto> steps, OnboardingStepKey key)
    {
        string wireKey = WireKeyOf(key);
        OnboardingStepDto? step = steps.FirstOrDefault(s => s.Key == wireKey);

        return step?.State is States.Available or States.InProgress or States.Rejected;
    }

    /// <summary>Pasul cerut e finalizat. Pereche cu <see cref="IsWritableByUser"/>, aceeași sursă.</summary>
    public static bool IsCompleted(IReadOnlyList<OnboardingStepDto> steps, OnboardingStepKey key)
    {
        string wireKey = WireKeyOf(key);
        return steps.FirstOrDefault(s => s.Key == wireKey)?.Status == StatusCompleted;
    }

    public static string WireKeyOf(OnboardingStepKey key) =>
        Steps.Single(s => s.Key == key).WireKey;

    public static string LabelOf(OnboardingStepKey key) =>
        Steps.Single(s => s.Key == key).Label;

    public static List<OnboardingStepDto> BuildSteps(
        PfaRegistration? registration,
        OnboardingSectionStatus pfaStatus,
        OnboardingEligibilityProfile? eligibility)
    {
        // 1) Statusul „propriu” al fiecărui pas, înainte de gating.
        string[] own =
        [
            EligibilityStatusOf(eligibility),
            PfaStatusOf(registration, pfaStatus),
            FiscalStatusOf(registration),
            ArrStatusOf(registration),
            PlatformsStatusOf(registration),
            VehicleStatusOf(registration),
        ];

        // Semnale auxiliare, folosite doar pentru vocabularul fin (`State`).
        bool[] started =
        [
            eligibility is not null,
            HasStartedPfa(registration),
            HasStartedFiscal(registration),
            registration?.ArrAuthorizationRequest is not null,
            registration?.PlatformAccounts.Exists(p => p.IsSelectedByUser) == true,
            registration?.Vehicles.Count > 0,
        ];

        bool[] rejected =
        [
            eligibility?.Status == EligibilityStatus.Ineligible,
            pfaStatus == OnboardingSectionStatus.Rejected,
            registration?.SignaturePacket?.Status == SignaturePacketStatus.Rejected,
            SectionRejected(registration, OnboardingSectionKey.AutorizatieTransport),
            false,
            SectionRejected(registration, OnboardingSectionKey.CopieConforma)
                || SectionRejected(registration, OnboardingSectionKey.Vehicul),
        ];

        // 2) Deblocare liniară: un pas rămâne blocat cât timp predecesorul lui nu e finalizat.
        var result = new List<OnboardingStepDto>(Steps.Length);
        bool predecessorDone = true;

        foreach (StepDef def in Steps)
        {
            int order = (int)def.Key;
            string status = own[order];
            string? blockReason = null;

            if (!predecessorDone && status != StatusCompleted)
            {
                status = StatusLocked;
                blockReason = $"Finalizează întâi pasul „{Steps[order - 1].Label}”.";
            }

            result.Add(new OnboardingStepDto(
                order,
                def.WireKey,
                def.Label,
                status,
                blockReason,
                def.Path,
                StateOf(status, started[order], rejected[order]),
                def.OwnedBy));

            predecessorDone = status == StatusCompleted;
        }

        return result;
    }

    /// <summary>
    /// Traduce statusul istoric în vocabularul fin. Respingerea are prioritate peste „în lucru”:
    /// un pas respins e tot al userului, dar cu un motiv de arătat.
    /// </summary>
    private static string StateOf(string status, bool started, bool rejected) => status switch
    {
        StatusLocked => States.Locked,
        StatusCompleted => States.Completed,
        StatusAwaitingValidation => States.PendingAdmin,
        _ when rejected => States.Rejected,
        _ when started => States.InProgress,
        _ => States.Available,
    };

    private static bool SectionRejected(PfaRegistration? registration, OnboardingSectionKey key) =>
        registration?.OnboardingSections
            .SingleOrDefault(s => s.SectionKey == key)?.Status == OnboardingSectionStatus.Rejected;

    private static bool HasStartedPfa(PfaRegistration? r) =>
        r is not null && (r.CompanyFormationRequest is not null || !string.IsNullOrWhiteSpace(r.Cui));

    private static bool HasStartedFiscal(PfaRegistration? r) =>
        r?.FiscalProfile is not null || r?.BankAccountDeclaration is not null || r?.OblioAccount is not null;

    private static string EligibilityStatusOf(OnboardingEligibilityProfile? profile) => profile?.Status switch
    {
        EligibilityStatus.Eligible => StatusCompleted,
        _ => StatusInProgress,
    };

    private static string PfaStatusOf(PfaRegistration? registration, OnboardingSectionStatus pfaStatus)
    {
        string status = pfaStatus switch
        {
            OnboardingSectionStatus.Validated => StatusCompleted,
            OnboardingSectionStatus.AwaitingValidation => StatusAwaitingValidation,
            _ => StatusInProgress,
        };

        if (registration?.RegistrationType != RegistrationType.NuAmPfa)
        {
            return status;
        }

        // Pe ramura „Nu am PFA” pasul rămâne al șoferului până semnează dosarul de înființare:
        // plata singură nu înseamnă că avem datele cu care se depune la ONRC.
        CompanyFormationStatus? formation = registration.CompanyFormationRequest?.Status;
        bool signed = formation is not null
            and not CompanyFormationStatus.Draft
            and not CompanyFormationStatus.InfoRequested;

        return signed ? status : StatusInProgress;
    }

    /// <summary>
    /// Partea pe care o poate face singur șoferul: răspunsul la TVA, contul bancar declarat și
    /// consimțămintele Oblio. Verificarea contului și pachetul de semnături nu intră aici — alea
    /// sunt ale adminului.
    /// </summary>
    public static bool FiscalUserPartComplete(PfaRegistration? r) =>
        r?.BankAccountDeclaration is not null
        && r.OblioAccount?.AllConsentsAccepted == true
        // Doar un răspuns ferm contează. „DontKnow” e o valoare istorică: dosarele vechi rămân
        // deschise până când clientul răspunde Da/Nu.
        && r.FiscalProfile?.VatAnswer is VatAnswer.Yes or VatAnswer.No;

    private static string FiscalStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return StatusInProgress;
        }

        OnboardingSignaturePacket? packet = r.SignaturePacket;

        // Închiderea pasului cere și pachetul de semnături alocat de admin — altfel șoferul ar
        // trece mai departe fără împuternicirile cu care depunem dosarele în numele lui.
        if (packet?.Status == SignaturePacketStatus.Completed
            && r.BankAccountDeclaration?.Status == BankDeclarationStatus.Verified
            && FiscalUserPartComplete(r))
        {
            return StatusCompleted;
        }

        // Respins de admin ⇒ mingea se întoarce la șofer, cu motiv.
        if (packet?.Status == SignaturePacketStatus.Rejected)
        {
            return StatusInProgress;
        }

        return packet?.SubmittedForReviewAtUtc is not null
            ? StatusAwaitingValidation
            : StatusInProgress;
    }

    private static string ArrStatusOf(PfaRegistration? r)
    {
        ArrAuthorizationStatus? status = r?.ArrAuthorizationRequest?.Status;
        return status == ArrAuthorizationStatus.Issued ? StatusCompleted : StatusInProgress;
    }

    private static string PlatformsStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return StatusInProgress;
        }

        var selected = r.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .ToList();

        if (selected.Count == 0)
        {
            return StatusInProgress;
        }

        // Șoferul a terminat partea lui când toate platformele alese au credențialele complete.
        // Activarea în Uber/Bolt rămâne treaba adminului, dar nu ține userul pe loc.
        if (selected.All(PlatformShared.UserPartComplete))
        {
            return StatusCompleted;
        }

        return selected.All(p => p.OnboardingStatus == PfaPlatformOnboardingStatus.Active)
            ? StatusCompleted
            : StatusInProgress;
    }

    private static string VehicleStatusOf(PfaRegistration? r)
    {
        PfaVehicle? vehicle = r?.Vehicles
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault();

        if (vehicle?.CopyRequest?.Status == VehicleCopyRequestStatus.Issued)
        {
            return StatusCompleted;
        }

        return StatusInProgress;
    }
}
