using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

public sealed record OnboardingSectionDto(
    string Key,
    string Status,
    string? Note,
    DateTime? SubmittedAtUtc,
    DateTime? ValidatedAtUtc);

/// <summary>Un pas al onboardingului (6 pași), cu status derivat din frunzele/semnalele lui.</summary>
public sealed record OnboardingStepDto(
    int Order,
    string Key,
    string Label,
    string Status,
    string? BlockReason,
    string Path);

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
    // Ramura „Nu am PFA": unde a rămas dosarul de înființare, ca pasul PFA să trimită direct
    // în etapa potrivită. Null pentru „Am PFA".
    string? CompanyFormationStatus = null,
    string? CompanyFormationStage = null,
    // DOAR PENTRU TESTARE — de șters odată cu SkipOnboardingStepCommand.
    bool TestSkipEnabled = false);

internal static class OnboardingStateBuilder
{
    private static readonly OnboardingSectionKey[] DocumentSections =
    [
        OnboardingSectionKey.AutorizatieTransport,
        OnboardingSectionKey.CopieConforma,
        OnboardingSectionKey.Vehicul,
    ];

    /// <summary>
    /// Derivă starea completă de onboarding: secțiunea PFA din PfaRegistration
    /// (nu are rând propriu), secțiunile 2–4 din rândurile OnboardingSectionApproval.
    /// </summary>
    public static OnboardingStateResponse Build(
        PfaRegistration? registration,
        bool hasPaidInfiintare,
        OnboardingEligibilityProfile? eligibility = null)
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

        return new OnboardingStateResponse(
            registration?.Id,
            registration?.Status.ToString(),
            registration?.RegistrationType.ToString(),
            registration?.ReviewNote,
            hasPaidInfiintare,
            sections,
            allStepsCompleted,
            steps,
            registration?.CompanyFormationRequest?.Status.ToString(),
            registration?.CompanyFormationRequest?.CurrentStage.ToString());
    }
}
