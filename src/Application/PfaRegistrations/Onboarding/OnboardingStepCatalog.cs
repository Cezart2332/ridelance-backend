using Application.PfaRegistrations.Onboarding.Platforms;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Gruparea onboardingului în 6 pași și derivarea statusului fiecărui pas din frunzele/semnalele
/// lui (secțiuni de documente + entitățile ghidate). Statusul unui pas NU se stochează — se derivă
/// mereu aici, la citire. Ordinea și deblocarea (fiecare blocare explică motivul) trăiesc tot aici.
///
/// Ordinea e liniară, dar deblocarea NU mai așteaptă adminul: pasul N se deschide când șoferul și-a
/// terminat partea din N-1, nu când adminul l-a validat. Înainte, poarta cerea „Completed", iar 5
/// din 6 pași se închid doar din admin — deci dosarul depus la pasul PFA oprea șoferul zile întregi,
/// iar pasul 5 nu ducea niciodată la 6. Validarea rămâne obligatorie, dar se face în paralel:
/// înrolarea (<see cref="AllCompleted"/>) cere în continuare toți pașii finalizați.
///
/// Singura excepție e pachetul de semnături (RL-02): „arr” rămâne blocat până când adminul îl alocă,
/// fiindcă împuternicirea din pachet e chiar actul cu care se depune dosarul ARR.
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
        // Conturile de operator se activează manual din admin. Eticheta e „Uber & Bolt", nu
        // „Uber Fleet & Bolt Fleet": pasul cere DOUĂ conturi pe platformă — cel de flotă și cel de
        // șofer — iar antetul cu „Fleet" stătea deasupra ecranelor de șofer și le contrazicea.
        new(OnboardingStepKey.Platforms, "platforms", "Uber & Bolt", "/onboarding/platforms", Owners.Admin),
        // Copia conformă se emite pe autorizația de transport.
        new(OnboardingStepKey.Vehicle, "vehicle", "Vehicul, copie conformă & ecusoane", "/onboarding/vehicle",
            Owners.Admin),
    ];

    /// <summary>Toți cei 6 pași sunt finalizați — condiția reală de înrolare.</summary>
    public static bool AllCompleted(IReadOnlyList<OnboardingStepDto> steps) =>
        steps.Count > 0 && steps.All(s => s.Status == StatusCompleted);

    /// <summary>
    /// Cheia pasului la care mai are șoferul ceva de făcut: primul cu partea lui neterminată.
    /// <c>null</c> când și-a făcut peste tot partea, chiar dacă adminul încă validează.
    ///
    /// NU e „primul nefinalizat": un pas trimis la validare rămâne nefinalizat săptămâni, iar
    /// frontendul folosește valoarea asta ca țintă de navigare — l-ar fi trimis mereu înapoi în
    /// pasul pe care tocmai îl predase.
    /// </summary>
    public static string? CurrentStepKey(IReadOnlyList<OnboardingStepDto> steps) =>
        steps.FirstOrDefault(s => !s.UserPartDone)?.Key;

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

        // Partea șoferului, separat de verdictul adminului. Asta deschide pasul următor.
        bool[] userDone =
        [
            own[0] == StatusCompleted,
            // Dosarul PFA e depus: predat spre validare sau deja validat. Ramura „Nu am PFA" e
            // acoperită de `PfaStatusOf`, care ține pasul în `InProgress` până se semnează dosarul
            // de înființare — deci nici aici nu trece mai devreme.
            own[1] is StatusAwaitingValidation or StatusCompleted,
            // Excepția RL-02: pachetul de semnături e al adminului, iar împuternicirea din el e
            // actul cu care se depune dosarul ARR. Fără el, pasul următor n-are ce depune.
            own[2] == StatusCompleted,
            // Dosarul ARR e depus; autorizația o emite ARR, nu șoferul.
            registration?.ArrAuthorizationRequest?.SubmittedAtUtc is not null,
            PlatformsUserPartDone(registration),
            // Ultimul pas: n-are succesor de deblocat.
            false,
        ];

        // 2) Deblocare liniară: un pas rămâne blocat cât timp predecesorul lui nu e gata de predat.
        var result = new List<OnboardingStepDto>(Steps.Length);
        bool predecessorOpen = true;

        foreach (StepDef def in Steps)
        {
            int order = (int)def.Key;
            string status = own[order];
            string? blockReason = null;

            if (!predecessorOpen && status != StatusCompleted)
            {
                status = StatusLocked;
                // Singurul pas care mai blochează după ce șoferul și-a făcut treaba e cel fiscal,
                // și acolo așteptarea e a noastră — mesajul trebuie s-o spună, nu să-i ceară lui
                // să termine ceva ce a terminat deja.
                blockReason = own[order - 1] == StatusAwaitingValidation
                    ? $"Așteptăm să finalizăm pasul „{Steps[order - 1].Label}”. Te anunțăm când e gata."
                    : $"Finalizează întâi pasul „{Steps[order - 1].Label}”.";
            }

            result.Add(new OnboardingStepDto(
                order,
                def.WireKey,
                def.Label,
                status,
                blockReason,
                def.Path,
                StateOf(status, started[order], rejected[order]),
                def.OwnedBy,
                userDone[order] || status == StatusCompleted));

            predecessorOpen = status == StatusCompleted || userDone[order];
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

    /// <summary>
    /// Șoferul a terminat partea lui de pas 5: a ales cel puțin o platformă și a completat
    /// credențialele pentru toate cele alese. Activarea în Uber/Bolt rămâne a adminului.
    /// </summary>
    private static bool PlatformsUserPartDone(PfaRegistration? r)
    {
        if (r is null)
        {
            return false;
        }

        var selected = r.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .ToList();

        return selected.Count > 0 && selected.TrueForAll(PlatformShared.UserPartComplete);
    }

    private static string PlatformsStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return StatusInProgress;
        }

        if (PlatformsUserPartDone(r))
        {
            return StatusCompleted;
        }

        var selected = r.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .ToList();

        return selected.Count > 0
            && selected.TrueForAll(p => p.OnboardingStatus == PfaPlatformOnboardingStatus.Active)
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
