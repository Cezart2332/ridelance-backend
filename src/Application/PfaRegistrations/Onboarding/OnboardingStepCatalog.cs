using Application.PfaRegistrations.Onboarding.Platforms;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Gruparea onboardingului în 6 pași și derivarea statusului fiecărui pas din frunzele/semnalele
/// lui (secțiuni de documente + entitățile ghidate). Statusul unui pas NU se stochează — se derivă
/// mereu aici, la citire. Ordinea și deblocarea (fiecare blocare explică motivul) trăiesc tot aici.
///
/// Ordinea e liniară și fiecare pas se deblochează pe validarea adminului: pasul N se deschide abia
/// când un om a verificat și validat pasul N-1. Șoferul își termină partea, pasul trece în
/// verificare, iar el așteaptă acolo.
///
/// A fost o vreme invers — pașii se deschideau de îndată ce șoferul își făcea partea, iar validarea
/// venea în paralel. S-a renunțat: fiecare pas trebuie verificat înainte să se construiască ceva pe
/// el, iar dosarele (ARR, copie conformă) se generează doar din acte verificate de om.
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
    /// Cheia pasului la care se află șoferul: primul pe care adminul nu l-a validat încă.
    /// <c>null</c> doar când toți pașii sunt validați.
    ///
    /// Fiecare pas se deblochează pe validarea adminului, deci un pas predat spre verificare e și
    /// singurul loc unde poate sta șoferul — următorul e închis. Înainte se lua primul cu partea
    /// șoferului neterminată, fiindcă pașii se deschideau fără să aștepte adminul.
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

        // Un pas în verificare rămâne al șoferului cât timp adminul nu l-a validat: validarea se
        // face în paralel cu restul onboardingului, deci o corectură făcută între timp e exact ce
        // vrem să vadă adminul. Excepția e dosarul PFA — odată predat, îl depunem noi (RL-01).
        return step?.State is States.Available or States.InProgress or States.Rejected
            || step?.State == States.PendingAdmin && key != OnboardingStepKey.Pfa;
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

    /// <summary>
    /// Documentele cu care șoferul își încheie partea de la pasul 1. Verdictul rămâne al nostru —
    /// verificarea rulează în fundal și îl anunțăm dacă e ceva de refăcut.
    /// </summary>
    private static readonly DocumentCategory[][] EligibilityDocuments =
    [
        [DocumentCategory.CarteIdentitate],
        [DocumentCategory.PermisConducere],
        [DocumentCategory.AtestatSofer, DocumentCategory.AtestatTransport],
    ];

    public static List<OnboardingStepDto> BuildSteps(
        PfaRegistration? registration,
        OnboardingSectionStatus pfaStatus,
        OnboardingEligibilityProfile? eligibility,
        IReadOnlyList<Document>? documents = null)
    {
        // 1) Statusul „propriu” al fiecărui pas, înainte de gating.
        string[] own =
        [
            EligibilityStatusOf(eligibility, documents),
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
            // Verdictul automat „neeligibil" contează abia cu toate actele încărcate. Profilul îl
            // creează OCR-ul la PRIMUL document și rămâne „neeligibil" până apare atestatul — deci
            // după buletin pasul apărea respins, cu „încarcă atestatul", înainte ca omul să fi
            // apucat să-l încarce. Lipsa unui act încă neîncărcat nu e o respingere.
            EligibilityAdminRejectionOpen(eligibility, documents)
                || eligibility?.Status == EligibilityStatus.Ineligible && EligibilityDocumentsUploaded(documents),
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
            // Pasul 1 se încheie când documentele sunt încărcate, nu când OCR-ul a reușit să
            // citească din ele. Altfel un dosar cu toate actele la locul lor rămâne blocat până
            // când un model se descurcă cu o poză — iar verdictul oricum vine după, prin
            // notificare. Singurul lucru care ține pe loc e un refuz ferm.
            // O respingere din admin, încă necorectată, întoarce pasul la șofer: altfel ar rămâne
            // „terminat" și n-ar mai fi trimis înapoi în el.
            own[0] == StatusCompleted
                || (own[0] == StatusAwaitingValidation || EligibilityUserPartDone(eligibility, documents))
                    && !EligibilityAdminRejectionOpen(eligibility, documents),
            // Dosarul PFA e depus: predat spre validare sau deja validat. Ramura „Nu am PFA" e
            // acoperită de `PfaStatusOf`, care ține pasul în `InProgress` până se semnează dosarul
            // de înființare — deci nici aici nu trece mai devreme.
            own[1] is StatusAwaitingValidation or StatusCompleted,
            // Șoferul a trimis pasul fiscal la verificare. Pachetul de semnături vine de la noi, pe
            // email, dar nu mai ține pe loc restul onboardingului: omul completează mai departe
            // cât îl pregătim, iar adminul validează pașii în paralel.
            own[2] is StatusAwaitingValidation or StatusCompleted,
            // Dosarul ARR e depus; autorizația o emite ARR, nu șoferul.
            own[3] == StatusCompleted
                || registration?.ArrAuthorizationRequest?.SubmittedAtUtc is not null
                    && !SectionRejected(registration, OnboardingSectionKey.AutorizatieTransport),
            PlatformsUserPartDone(registration),
            // Ultimul pas n-are succesor de deblocat, dar semnalul contează: fără el, șoferul
            // n-ar ajunge niciodată la ecranul de final, ci ar fi trimis înapoi în pasul ăsta.
            own[5] == StatusCompleted || VehicleUserPartDone(registration, documents),
        ];

        // 2) Deblocare liniară, pe verdictul adminului: pasul N se deschide abia când adminul a
        //    validat pasul N-1. Nu mai ajunge ca șoferul să-și fi terminat partea — fiecare pas
        //    se verifică de un om înainte să treacă mai departe.
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
                // Când șoferul și-a făcut deja partea, așteptarea e a noastră — mesajul trebuie s-o
                // spună, nu să-i ceară să termine ceva ce a terminat.
                blockReason = userDone[order - 1]
                    ? $"Verificăm pasul „{Steps[order - 1].Label}”. Te anunțăm când e validat și se deschide acesta."
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

            predecessorOpen = status == StatusCompleted;
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

    /// <summary>
    /// Pasul 1 se bifează doar pe validarea din admin. Evaluarea automată (`Status`) vine din
    /// datele extrase, iar datele extrase nu decid nimic: cu actele încărcate, pasul stă în
    /// verificare (clepsidra) până se uită cineva.
    /// </summary>
    private static string EligibilityStatusOf(OnboardingEligibilityProfile? profile, IReadOnlyList<Document>? documents)
    {
        if (profile?.AdminValidatedAtUtc is not null)
        {
            return StatusCompleted;
        }

        if (EligibilityAdminRejectionOpen(profile, documents))
        {
            return StatusInProgress;
        }

        return EligibilityUserPartDone(profile, documents) ? StatusAwaitingValidation : StatusInProgress;
    }

    /// <summary>
    /// Adminul a respins pasul 1, iar șoferul n-a reîncărcat încă niciun act după respingere. Un act
    /// nou încărcat după respingere redeschide verificarea — altfel respingerea n-ar avea ieșire.
    /// </summary>
    public static bool EligibilityAdminRejectionOpen(OnboardingEligibilityProfile? profile, IReadOnlyList<Document>? documents)
    {
        if (profile?.AdminRejectedAtUtc is not DateTime rejectedAt || profile.AdminValidatedAtUtc is not null)
        {
            return false;
        }

        return documents is null || !documents.Any(d =>
            EligibilityDocuments.Any(categories => categories.Contains(d.Category))
            && d.UploadedAtUtc > rejectedAt);
    }

    /// <summary>
    /// Șoferul și-a încărcat cele trei acte de la pasul 1 și niciunul nu l-a descalificat.
    ///
    /// Fără lista de documente (apelanții care nu o au) rămâne pe verdictul profilului — adică pe
    /// comportamentul dinainte, nu pe o presupunere optimistă.
    /// </summary>
    public static bool EligibilityUserPartDone(
        OnboardingEligibilityProfile? profile,
        IReadOnlyList<Document>? documents)
    {
        return profile?.Status != EligibilityStatus.Ineligible && EligibilityDocumentsUploaded(documents);
    }

    /// <summary>Cele trei acte de la pasul 1 sunt încărcate și niciunul nu e respins.</summary>
    public static bool EligibilityDocumentsUploaded(IReadOnlyList<Document>? documents) =>
        documents is not null
        && Array.TrueForAll(
            EligibilityDocuments,
            categories => documents.Any(d =>
                categories.Contains(d.Category) && d.Status != DocumentStatus.Rejected));

    /// <summary>
    /// Partea șoferului la pasul 2, pe ramura „am deja PFA": cele două certificate de la ONRC.
    ///
    /// Contează fiindcă pasul trece la noi de îndată ce dosarul e deschis, iar de acolo nu mai
    /// acceptă scrieri (RL-01). Fără verificarea asta, dosarul se preda în secunda în care omul
    /// răspundea „Da" — înainte să apuce să încarce certificatele.
    /// </summary>
    public static bool PfaUserPartDone(PfaRegistration? registration, IReadOnlyList<Document>? documents)
    {
        if (registration is null)
        {
            return false;
        }

        if (registration.RegistrationType != RegistrationType.AmPfa || documents is null)
        {
            return true;
        }

        return documents.Any(d => d.Category == DocumentCategory.CertificatInregistrare
                && d.Status != DocumentStatus.Rejected)
            && documents.Any(d => d.Category == DocumentCategory.CertificatConstatator
                && d.Status != DocumentStatus.Rejected);
    }

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
    /// Partea pe care o poate face singur șoferul la pasul fiscal: răspunsul la TVA și
    /// consimțămintele Oblio. Pachetul de semnături e al adminului și rămâne în afara ei.
    ///
    /// Contul bancar NU mai intră aici. Ramura „nu am cont, am nevoie de unul" trimite omul la
    /// bancă, iar contul apare zile mai târziu — până atunci pasul rămânea neterminat, butonul de
    /// trimitere la verificare nu apărea, iar validarea adminului nu avea ce închide. Contul se
    /// cere oricum înainte de primele încasări, doar că nu de aici.
    /// </summary>
    public static bool FiscalUserPartComplete(PfaRegistration? r) =>
        r?.OblioAccount?.AllConsentsAccepted == true
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

        // Pasul se închide pe verdictul adminului, și numai pe el.
        //
        // Cerea până acum și `FiscalUserPartComplete` pe deasupra, ceea ce însemna că „Validează
        // secțiunea" nu închidea nimic dacă lipsea răspunsul la TVA sau un consimțământ Oblio:
        // adminul apăsa, primea „pasul următor al clientului este deblocat", iar clientul rămânea
        // exact unde era, fără ca cineva să vadă de ce. Adminul se uită la dosar când validează;
        // dacă ceva lipsește, are butonul de respingere, cu motiv.
        //
        // Contul bancar nu se cere `Verified`: statusul ăla îl pune un singur lucru, potrivirea
        // IBAN-ului declarat cu un cont legat prin Open Banking. Cine declară contul de mână —
        // drumul obișnuit — rămânea pe `Pending` orice ar fi făcut adminul.
        if (packet?.Status == SignaturePacketStatus.Completed)
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

    /// <summary>
    /// Pasul ARR se bifează când adminul validează secțiunea „Autorizație transport" — sau când
    /// autorizația emisă e înregistrată. Înainte doar a doua variantă conta, iar „Validează" din
    /// admin scria pe secțiune fără ca pasul să se schimbe: adminul vedea „validat", șoferul nu.
    /// </summary>
    private static string ArrStatusOf(PfaRegistration? r)
    {
        if (r?.ArrAuthorizationRequest?.Status == ArrAuthorizationStatus.Issued
            || SectionValidated(r, OnboardingSectionKey.AutorizatieTransport))
        {
            return StatusCompleted;
        }

        if (SectionRejected(r, OnboardingSectionKey.AutorizatieTransport))
        {
            return StatusInProgress;
        }

        return r?.ArrAuthorizationRequest?.SubmittedAtUtc is not null ? StatusAwaitingValidation : StatusInProgress;
    }

    private static bool SectionValidated(PfaRegistration? registration, OnboardingSectionKey key) =>
        registration?.OnboardingSections
            .SingleOrDefault(s => s.SectionKey == key)?.Status == OnboardingSectionStatus.Validated;

    /// <summary>
    /// Partea șoferului la ultimul pas: dosarul depus, apoi copia conformă și ecusoanele primite,
    /// încărcate înapoi.
    ///
    /// Regresia pe care o ține pe loc: se considera terminată de îndată ce dosarul era depus. Dar
    /// copia conformă și ecusoanele vin DUPĂ depunere — ecranele lor apar abia atunci — iar în
    /// secunda în care apăreau, pasul curent devenea „niciunul" și șoferul era trimis la ecranul „Ai
    /// terminat onboardingul". Nu le mai vedea deloc.
    ///
    /// Ecusoanele se cer doar pentru platformele alese: un ecuson Bolt n-are ce căuta la cineva care
    /// lucrează numai pe Uber. Fără lista de documente răspunsul e „nu", ca la pasul 1 — nu o
    /// presupunere optimistă.
    /// </summary>
    public static bool VehicleUserPartDone(PfaRegistration? registration, IReadOnlyList<Document>? documents)
    {
        if (registration is null
            || documents is null
            || LatestCopyRequest(registration)?.SubmittedAtUtc is null
            || SectionRejected(registration, OnboardingSectionKey.CopieConforma)
            || SectionRejected(registration, OnboardingSectionKey.Vehicul))
        {
            return false;
        }

        if (!HasUsableDocument(documents, DocumentCategory.CopieConforma))
        {
            return false;
        }

        return registration.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .Select(p => BadgeCategoryOf(p.Provider))
            .OfType<DocumentCategory>()
            .Distinct()
            .All(category => HasUsableDocument(documents, category));
    }

    private static bool HasUsableDocument(IReadOnlyList<Document> documents, DocumentCategory category) =>
        documents.Any(d => d.Category == category && d.Status != DocumentStatus.Rejected);

    /// <summary>Ecusonul fiecărei platforme. Null pentru una fără ecuson, ca să nu ceară nimic în plus.</summary>
    private static DocumentCategory? BadgeCategoryOf(PfaPlatformProvider provider) => provider switch
    {
        PfaPlatformProvider.Uber => DocumentCategory.EcusonUber,
        PfaPlatformProvider.Bolt => DocumentCategory.EcusonBolt,
        _ => null,
    };

    private static VehicleCopyRequest? LatestCopyRequest(PfaRegistration? r) =>
        r?.Vehicles
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefault()?.CopyRequest;

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

    /// <summary>
    /// Pasul Uber &amp; Bolt se bifează când adminul activează conturile alese. Cu datele completate
    /// de șofer, stă în verificare — înainte se bifa singur, fără ca cineva să se fi uitat.
    /// </summary>
    private static string PlatformsStatusOf(PfaRegistration? r)
    {
        if (r is null)
        {
            return StatusInProgress;
        }

        var selected = r.PlatformAccounts
            .Where(p => p.IsSelectedByUser)
            .ToList();

        if (selected.Count > 0 && selected.TrueForAll(p => p.OnboardingStatus == PfaPlatformOnboardingStatus.Active))
        {
            return StatusCompleted;
        }

        return PlatformsUserPartDone(r) ? StatusAwaitingValidation : StatusInProgress;
    }

    /// <summary>
    /// Ultimul pas se bifează când adminul validează ambele secțiuni (copia conformă și documentele
    /// mașinii) — sau când copia conformă emisă e înregistrată. Dosarul depus îl pune în verificare.
    /// </summary>
    private static string VehicleStatusOf(PfaRegistration? r)
    {
        VehicleCopyRequest? copy = LatestCopyRequest(r);

        if (copy?.Status == VehicleCopyRequestStatus.Issued
            || SectionValidated(r, OnboardingSectionKey.CopieConforma) && SectionValidated(r, OnboardingSectionKey.Vehicul))
        {
            return StatusCompleted;
        }

        if (SectionRejected(r, OnboardingSectionKey.CopieConforma) || SectionRejected(r, OnboardingSectionKey.Vehicul))
        {
            return StatusInProgress;
        }

        return copy?.SubmittedAtUtc is not null ? StatusAwaitingValidation : StatusInProgress;
    }
}
