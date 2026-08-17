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
            // Două avize, nu unul: le emit instituții diferite (medicina muncii / cabinet de
            // psihologie) și expiră la date diferite, deci se cer, se validează și se urmăresc
            // separat. Vezi specul de fix-uri §7.
            new("Aviz medical", [DocumentCategory.AdeverintaMedicala]),
            new("Aviz psihologic", [DocumentCategory.AvizPsihologic]),
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

    /// <summary>
    /// Ce contract cere fiecare mod de deținere, peste documentele comune ale mașinii.
    ///
    /// Leasingul cere doar acordul finanțatorului pentru folosirea vehiculului în transport alternativ.
    /// </summary>
    private static readonly Dictionary<VehicleOwnershipMode, DocumentRequirement[]> OwnershipRequirements = new()
    {
        [VehicleOwnershipMode.Owned] = [],
        [VehicleOwnershipMode.Rented] =
        [
            new("Contract de închiriere", [DocumentCategory.ContractVehicul]),
        ],
        [VehicleOwnershipMode.Leased] =
        [
            new("Acord de leasing", [DocumentCategory.AcordLeasing]),
        ],
        [VehicleOwnershipMode.Comodat] =
        [
            new("Contract de comodat", [DocumentCategory.ContractVehicul]),
            // Acordul proprietarului se încarcă în aceeași categorie ca acordul finanțatorului:
            // pentru ARR e același tip de act — acordul celui care deține mașina.
            new("Acordul proprietarului", [DocumentCategory.AcordLeasing]),
        ],
        [VehicleOwnershipMode.AddedLater] = [],
    };

    /// <summary>
    /// Documentele obligatorii pentru mașină, în funcție de modul de deținere. Sursa unică:
    /// validarea secțiunii, checklistul și generatorul de dosar citesc toate de aici.
    /// </summary>
    public static IReadOnlyList<DocumentRequirement> RequirementsForVehicle(VehicleOwnershipMode mode)
    {
        DocumentRequirement[] ownership = OwnershipRequirements.TryGetValue(mode, out DocumentRequirement[]? own)
            ? own
            : [];

        return
        [
            .. ownership,
            .. RequirementsFor(OnboardingSectionKey.Vehicul)
                // Contractul vine din tabelul de mai sus, cu eticheta corectă pentru mod.
                .Where(r => !r.AcceptedCategories.Contains(DocumentCategory.ContractVehicul)),
        ];
    }

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
