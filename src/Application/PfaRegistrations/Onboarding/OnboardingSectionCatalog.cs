using Domain.Documents;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Documentele obligatorii ale secțiunilor validate de admin. A rămas doar secțiunea PFA, iar actele
/// ei se verifică pe dosar, nu pe listă. Cerințele pasului „ARR &amp; Cont Flotă” stau în
/// <c>ArrFleetRules</c>.
/// </summary>
public static class OnboardingSectionCatalog
{
    public sealed record DocumentRequirement(string Label, DocumentCategory[] AcceptedCategories);

    public static IReadOnlyList<DocumentRequirement> RequirementsFor(OnboardingSectionKey key) => [];

    public static OnboardingSectionKey? NextSection(OnboardingSectionKey key) => null;

    public static string SectionLabel(OnboardingSectionKey key) => key switch
    {
        OnboardingSectionKey.Pfa => "PFA",
        _ => key.ToString(),
    };
}
