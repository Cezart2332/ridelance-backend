using Application.PfaRegistrations.Onboarding.ArrFleet;
using Domain.Documents;

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
    /// <summary>
    /// Actele cerute de fiecare pas. Doar „ARR &amp; Cont Flotă” are o listă; contractul mașinii
    /// atârnă de modul de deținere, deci nu intră aici.
    /// </summary>
    private static readonly Dictionary<OnboardingStepKey, ArrFleetRules.Requirement[]> StepRequirements = new()
    {
        [OnboardingStepKey.ArrFleet] =
        [
            .. ArrFleetRules.PersonalDocuments,
            ArrFleetRules.PaymentProof,
            .. ArrFleetRules.VehicleDocuments,
        ],
    };

    public static List<OnboardingChecklistItemDto> Build(
        OnboardingStepKey step,
        IReadOnlyList<Document> documents)
    {
        if (!StepRequirements.TryGetValue(step, out ArrFleetRules.Requirement[]? requirements))
        {
            return [];
        }

        var items = new List<OnboardingChecklistItemDto>();

        foreach (ArrFleetRules.Requirement requirement in requirements)
        {
            Document? newest = documents
                .Where(d => d.Origin == DocumentOrigin.UserUpload
                    && d.Category == requirement.Category
                    && !d.IsSuperseded)
                .OrderByDescending(d => d.UploadedAtUtc)
                .FirstOrDefault();

            items.Add(new OnboardingChecklistItemDto(
                requirement.Category.ToString(),
                requirement.Label,
                StateOf(newest),
                // Motivul respingerii se afișează pe rând, nu într-un tooltip. Cel scris de om
                // bate verdictul automat; iar fără niciunul, rândul tot spune ce e de făcut.
                newest?.Status == DocumentStatus.Rejected
                    ? newest.ReviewNote ?? newest.AiSummary ?? "Respins de echipa RIDElance. Încarcă o variantă nouă."
                    : null));
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
