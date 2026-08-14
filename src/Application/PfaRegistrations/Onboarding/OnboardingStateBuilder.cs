using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding;

public sealed record OnboardingSectionDto(
    string Key,
    string Status,
    string? Note,
    DateTime? SubmittedAtUtc,
    DateTime? ValidatedAtUtc);

/// <summary>
/// Un item din checklistul pasului curent. <c>State</c>: `missing` | `uploaded` | `verifying` |
/// `rejected`. Rail-ul dreapta îl randează ca atare, fără să recalculeze nimic.
/// </summary>
public sealed record OnboardingChecklistItemDto(
    string Key,
    string Label,
    string State,
    string? Note);

/// <summary>
/// Un pas al onboardingului (6 pași), cu status derivat din frunzele/semnalele lui.
///
/// <c>Status</c> și <c>State</c> descriu același lucru în două vocabulare. <c>Status</c> e cel
/// istoric, păstrat cât timp mai există consumatori pe el; <c>State</c> e cel din specul v3, care
/// separă „disponibil” de „în lucru” și scoate respingerea din frontend pe server. Ambele se derivă
/// din aceeași sursă, deci nu pot diverge.
/// </summary>
public sealed record OnboardingStepDto(
    int Order,
    string Key,
    string Label,
    string Status,
    string? BlockReason,
    string Path,
    // locked | available | in_progress | pending_admin | completed | rejected
    string State = "locked",
    // user | admin — cine face tranziția finală a pasului.
    string OwnedBy = "user",
    // Ce mai lipsește din pas. Populat doar pentru pasul curent — restul n-au consumator.
    IReadOnlyList<OnboardingChecklistItemDto>? Checklist = null);

public sealed record OnboardingStateResponse(
    Guid? PfaRegistrationId,
    string? PfaStatus,
    string? RegistrationType,
    string? PfaReviewNote,
    bool HasPaidInfiintare,
    List<OnboardingSectionDto> Sections,
    bool AllSectionsValidated,
    // Proiecția pe 6 pași (grupare în cod, status derivat — niciodată stocat).
    List<OnboardingStepDto> Steps,
    // Cheia pasului la care e oprit șoferul acum — singurul pe care se poate scrie. Null când
    // onboardingul e complet. Frontendul nu mai calculează asta singur.
    string? CurrentStep = null,
    // RL-03 — plata înființării vine DUPĂ completare. `CanPay` e adevărat abia când dosarul de
    // înființare e semnat: fără el n-avem ce depune la ONRC, deci n-avem ce încasa.
    bool CanPay = false,
    // NOT_REQUIRED (ramura „Am PFA") | PENDING | PAID | FAILED
    string PaymentStatus = "NOT_REQUIRED",
    // Ramura „Nu am PFA": unde a rămas dosarul de înființare, ca pasul PFA să trimită direct
    // în etapa potrivită. Null pentru „Am PFA".
    string? CompanyFormationStatus = null,
    string? CompanyFormationStage = null,
    // DOAR PENTRU TESTARE — de șters odată cu SkipOnboardingStepCommand.
    bool TestSkipEnabled = false);

/// <summary>
/// Public, nu internal: <see cref="CanPayInfiintare"/> e poarta de plată folosită și din afara
/// onboardingului (crearea sesiunii Stripe) și e acoperită de teste.
/// </summary>
public static class OnboardingStateBuilder
{
    private static readonly OnboardingSectionKey[] DocumentSections =
    [
        OnboardingSectionKey.AutorizatieTransport,
        OnboardingSectionKey.CopieConforma,
        OnboardingSectionKey.Vehicul,
    ];

    /// <summary>
    /// Dosarul de înființare e semnat, deci poate fi depus — singura condiție pentru plată.
    /// Aceeași funcție gardează și crearea sesiunii Stripe, ca UI-ul și API-ul să nu poată
    /// ajunge la concluzii diferite.
    /// </summary>
    public static bool CanPayInfiintare(PfaRegistration? registration, bool hasPaidInfiintare)
    {
        if (registration?.RegistrationType != RegistrationType.NuAmPfa || hasPaidInfiintare)
        {
            return false;
        }

        CompanyFormationStatus? formation = registration.CompanyFormationRequest?.Status;
        return formation is not null
            and not CompanyFormationStatus.Draft
            and not CompanyFormationStatus.InfoRequested;
    }

    private static string PaymentStatusOf(
        PfaRegistration? registration,
        bool hasPaidInfiintare,
        bool hasFailedPayment)
    {
        if (registration?.RegistrationType != RegistrationType.NuAmPfa)
        {
            return "NOT_REQUIRED";
        }

        if (hasPaidInfiintare)
        {
            return "PAID";
        }

        // O încercare eșuată nu resetează progresul; ecranul trebuie doar să ofere reîncercarea.
        return hasFailedPayment ? "FAILED" : "PENDING";
    }

    /// <summary>
    /// Derivă starea completă de onboarding: secțiunea PFA din PfaRegistration
    /// (nu are rând propriu), secțiunile 2–4 din rândurile OnboardingSectionApproval.
    /// </summary>
    public static OnboardingStateResponse Build(
        PfaRegistration? registration,
        bool hasPaidInfiintare,
        OnboardingEligibilityProfile? eligibility = null,
        bool hasFailedPayment = false,
        IReadOnlyList<Document>? documents = null)
    {
        OnboardingSectionStatus pfaStatus = registration switch
        {
            null => OnboardingSectionStatus.InProgress,
            { Status: PfaRegistrationStatus.Approved } => OnboardingSectionStatus.Validated,
            { Status: PfaRegistrationStatus.Rejected } => OnboardingSectionStatus.Rejected,
            { RegistrationType: RegistrationType.NuAmPfa } when !hasPaidInfiintare => OnboardingSectionStatus.InProgress,
            _ => OnboardingSectionStatus.AwaitingValidation,
        };

        var sections = new List<OnboardingSectionDto>
        {
            new(
                OnboardingSectionKey.Pfa.ToString(),
                pfaStatus.ToString(),
                pfaStatus == OnboardingSectionStatus.Rejected ? registration?.ReviewNote : null,
                registration?.CreatedAtUtc,
                registration?.ReviewedAtUtc),
        };

        foreach (OnboardingSectionKey key in DocumentSections)
        {
            OnboardingSectionApproval? row = registration?.OnboardingSections
                .SingleOrDefault(s => s.SectionKey == key);

            OnboardingSectionStatus status = row?.Status ?? OnboardingSectionStatus.Locked;

            sections.Add(new OnboardingSectionDto(
                key.ToString(),
                status.ToString(),
                row?.Note,
                row?.SubmittedAtUtc,
                row?.ValidatedAtUtc));
        }

        List<OnboardingStepDto> steps = OnboardingStepCatalog.BuildSteps(registration, pfaStatus, eligibility);

        // „Onboarding complet" = toți cei 6 pași finalizați (nu doar cele 3 secțiuni de documente).
        // Asta gateuiește redirectul spre plata abonamentului.
        bool allStepsCompleted = OnboardingStepCatalog.AllCompleted(steps);

        // Checklistul se compune doar pentru pasul curent: e singurul afișat în rail-ul dreapta,
        // iar restul ar însemna muncă și trafic degeaba.
        string? currentStepKey = OnboardingStepCatalog.CurrentStepKey(steps);
        if (currentStepKey is not null && documents is not null)
        {
            int index = steps.FindIndex(s => s.Key == currentStepKey);
            steps[index] = steps[index] with
            {
                Checklist = OnboardingChecklistBuilder.Build((OnboardingStepKey)steps[index].Order, documents),
            };
        }

        return new OnboardingStateResponse(
            registration?.Id,
            registration?.Status.ToString(),
            registration?.RegistrationType.ToString(),
            registration?.ReviewNote,
            hasPaidInfiintare,
            sections,
            allStepsCompleted,
            steps,
            currentStepKey,
            CanPayInfiintare(registration, hasPaidInfiintare),
            PaymentStatusOf(registration, hasPaidInfiintare, hasFailedPayment),
            registration?.CompanyFormationRequest?.Status.ToString(),
            registration?.CompanyFormationRequest?.CurrentStage.ToString());
    }
}
