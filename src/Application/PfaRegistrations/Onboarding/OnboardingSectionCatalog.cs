using Domain.Documents;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Sursa de adevăr server-side pentru documentele obligatorii ale fiecărei secțiuni
/// de onboarding. Fiecare intrare este un set de categorii acceptabile: cerința e
/// îndeplinită dacă userul are cel puțin un document non-respins în oricare din ele.
/// Oglindește configul de pe frontend (src/constants/documentSections.tsx).
/// </summary>
public static class OnboardingSectionCatalog
{
    public sealed record DocumentRequirement(string Label, DocumentCategory[] AcceptedCategories);

    private static readonly Dictionary<OnboardingSectionKey, DocumentRequirement[]> Requirements = new()
    {
        [OnboardingSectionKey.AutorizatieTransport] =
        [
            new("Certificat de înregistrare", [DocumentCategory.CertificatInregistrare]),
            new("Certificat constatator", [DocumentCategory.CertificatConstatator]),
            new("Atestat transport", [DocumentCategory.AtestatTransport, DocumentCategory.AtestatSofer]),
            new("Cazier judiciar", [DocumentCategory.CazierJudiciar]),
            new("Adeverință medicală", [DocumentCategory.AdeverintaMedicala]),
            new("Dovadă plată ARR", [DocumentCategory.DovadaPlataArr]),
        ],
        [OnboardingSectionKey.CopieConforma] =
        [
            new("Autorizație transport alternativ", [DocumentCategory.AutorizatieTransportAlternativ]),
            new("Talon / ITP", [DocumentCategory.Talon, DocumentCategory.ITP]),
            new("Carte identitate auto", [DocumentCategory.CarteIdentitateAuto]),
            new("Contract vehicul", [DocumentCategory.ContractVehicul]),
            new("Dovadă plată copie conformă & ecusoane", [DocumentCategory.DovadaPlataCopieConformaEcusoane]),
            // AcordLeasing e opțional („după caz”)
        ],
        [OnboardingSectionKey.Vehicul] =
        [
            new("Talon / ITP", [DocumentCategory.Talon, DocumentCategory.ITP]),
            new("RCA", [DocumentCategory.RCA]),
            new("Copie conformă", [DocumentCategory.CopieConforma]),
            new("Ecuson Uber", [DocumentCategory.EcusonUber]),
            new("Ecuson Bolt", [DocumentCategory.EcusonBolt]),
            new("Contract vehicul", [DocumentCategory.ContractVehicul]),
            // AsigurareCalatori e opțional
        ],
    };

    public static IReadOnlyList<DocumentRequirement> RequirementsFor(OnboardingSectionKey key) =>
        Requirements.TryGetValue(key, out DocumentRequirement[]? reqs) ? reqs : [];

    public static OnboardingSectionKey? NextSection(OnboardingSectionKey key) => key switch
    {
        OnboardingSectionKey.Pfa => OnboardingSectionKey.AutorizatieTransport,
        OnboardingSectionKey.AutorizatieTransport => OnboardingSectionKey.CopieConforma,
        OnboardingSectionKey.CopieConforma => OnboardingSectionKey.Vehicul,
        _ => null,
    };

    public static string SectionLabel(OnboardingSectionKey key) => key switch
    {
        OnboardingSectionKey.Pfa => "PFA",
        OnboardingSectionKey.AutorizatieTransport => "Autorizație transport",
        OnboardingSectionKey.CopieConforma => "Copie conformă & ecusoane",
        OnboardingSectionKey.Vehicul => "Vehicul",
        _ => key.ToString(),
    };
}
