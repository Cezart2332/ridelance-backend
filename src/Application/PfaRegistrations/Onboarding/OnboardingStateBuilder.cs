using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

public sealed record OnboardingSectionDto(
    string Key,
    string Status,
    string? Note,
    DateTime? SubmittedAtUtc,
    DateTime? ValidatedAtUtc);

public sealed record OnboardingStateResponse(
    Guid? PfaRegistrationId,
    string? PfaStatus,
    string? RegistrationType,
    string? PfaReviewNote,
    bool HasPaidInfiintare,
    List<OnboardingSectionDto> Sections,
    bool AllSectionsValidated);

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
    public static OnboardingStateResponse Build(PfaRegistration? registration, bool hasPaidInfiintare)
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

        bool allValidated = pfaStatus == OnboardingSectionStatus.Validated;

        foreach (OnboardingSectionKey key in DocumentSections)
        {
            OnboardingSectionApproval? row = registration?.OnboardingSections
                .SingleOrDefault(s => s.SectionKey == key);

            OnboardingSectionStatus status = row?.Status ?? OnboardingSectionStatus.Locked;
            allValidated &= status == OnboardingSectionStatus.Validated;

            sections.Add(new OnboardingSectionDto(
                key.ToString(),
                status.ToString(),
                row?.Note,
                row?.SubmittedAtUtc,
                row?.ValidatedAtUtc));
        }

        return new OnboardingStateResponse(
            registration?.Id,
            registration?.Status.ToString(),
            registration?.RegistrationType.ToString(),
            registration?.ReviewNote,
            hasPaidInfiintare,
            sections,
            allValidated);
    }
}
