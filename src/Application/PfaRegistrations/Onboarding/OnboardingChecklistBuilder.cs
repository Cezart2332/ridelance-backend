using Domain.Documents;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Ce mai lipsește din pasul curent, ca listă gata de afișat (RL-07 + rail-ul dreapta).
///
/// Se construiește pe server ca frontendul să nu recalculeze nimic: altfel inelul de progres și
/// lista de sub el ar putea ajunge la numere diferite. Numără doar documentele pe care șoferul le
/// vede — cele generate de noi există în dosar, dar nu sunt sarcina lui, deci nu au ce căuta în
/// „2 din 5".
/// </summary>
internal static class OnboardingChecklistBuilder
{
    /// <summary>Secțiunile de documente pe care le acoperă fiecare pas.</summary>
    private static readonly Dictionary<OnboardingStepKey, OnboardingSectionKey[]> StepSections = new()
    {
        [OnboardingStepKey.Arr] = [OnboardingSectionKey.AutorizatieTransport],
        [OnboardingStepKey.Vehicle] = [OnboardingSectionKey.CopieConforma, OnboardingSectionKey.Vehicul],
    };

    public static List<OnboardingChecklistItemDto> Build(
        OnboardingStepKey step,
        IReadOnlyList<Document> documents)
    {
        if (!StepSections.TryGetValue(step, out OnboardingSectionKey[]? sections))
        {
            return [];
        }

        var items = new List<OnboardingChecklistItemDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (OnboardingSectionKey section in sections)
        {
            foreach (OnboardingSectionCatalog.DocumentRequirement requirement in
                     OnboardingSectionCatalog.RequirementsFor(section))
            {
                // Aceeași cerință poate apărea în două secțiuni ale aceluiași pas (talon, contract).
                if (!seen.Add(requirement.Label))
                {
                    continue;
                }

                Document? newest = documents
                    .Where(d => d.Origin == DocumentOrigin.UserUpload
                        && requirement.AcceptedCategories.Contains(d.Category))
                    .OrderByDescending(d => d.UploadedAtUtc)
                    .FirstOrDefault();

                items.Add(new OnboardingChecklistItemDto(
                    requirement.AcceptedCategories[0].ToString(),
                    requirement.Label,
                    StateOf(newest),
                    // Motivul respingerii se afișează pe rând, nu într-un tooltip.
                    newest?.Status == DocumentStatus.Rejected ? newest.AiSummary : null));
            }
        }

        return items;
    }

    private static string StateOf(Document? document) => document switch
    {
        null => "missing",
        { Status: DocumentStatus.Rejected } => "rejected",
        { Status: DocumentStatus.Verified } => "uploaded",
        { AiStatus: DocumentAiStatus.Queued or DocumentAiStatus.Processing } => "verifying",
        _ => "verifying",
    };
}
