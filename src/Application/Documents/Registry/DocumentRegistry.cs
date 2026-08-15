using Domain.Documents;

namespace Application.Documents.Registry;

/// <summary>Cele trei categorii din spec §8: actele mele, actele firmei, actele mașinii.</summary>
public enum DocumentGroup
{
    Personal = 0,
    Pfa = 1,
    Vehicle = 2,
}

/// <param name="Key">Identificatorul stabil al tipului, folosit de frontend ca cheie de listă.</param>
/// <param name="Categories">
/// Categoriile care satisfac tipul. Sunt mai multe acolo unde același act a fost încărcat de-a
/// lungul timpului sub denumiri diferite — atestatul, de exemplu.
/// </param>
public sealed record DocumentTypeDef(
    string Key,
    string Label,
    DocumentGroup Group,
    IReadOnlyList<DocumentCategory> Categories,
    bool HasIssueDate,
    bool HasExpiryDate,
    bool IsOptional = false)
{
    public DocumentCategory PrimaryCategory => Categories[0];
}

/// <summary>
/// Catalogul documentelor pe cele trei pagini din Documente.
///
/// E o grupare nouă peste aceleași categorii, nu o colecție nouă de acte: documentele strânse
/// în onboarding se regăsesc aici automat, pentru că sunt aceleași rânduri din tabela
/// <c>Documents</c>. De asta „Lipsește" apare doar când chiar nu există înregistrare.
///
/// Catalogul stă pe server și pleacă spre client prin endpoint. Repo-ul are deja o pereche
/// oglindită manual — <c>OnboardingSectionCatalog.cs</c> și <c>documentSections.tsx</c> — care
/// trebuie ținută sincronizată de mână; o a treia copie ar fi fost o a treia ocazie de divergență.
/// </summary>
public static class DocumentRegistry
{
    public static readonly IReadOnlyList<DocumentTypeDef> All =
    [
        // ── Documente personale ──
        new("id-card", "Carte de identitate", DocumentGroup.Personal,
            [DocumentCategory.CarteIdentitate, DocumentCategory.Buletin], HasIssueDate: true, HasExpiryDate: true),
        new("driving-license", "Permis de conducere", DocumentGroup.Personal,
            [DocumentCategory.PermisConducere], HasIssueDate: true, HasExpiryDate: true),
        new("professional-certificate", "Atestat profesional", DocumentGroup.Personal,
            [DocumentCategory.AtestatTransport, DocumentCategory.AtestatSofer], HasIssueDate: true, HasExpiryDate: true),
        new("criminal-record", "Cazier judiciar", DocumentGroup.Personal,
            [DocumentCategory.CazierJudiciar], HasIssueDate: true, HasExpiryDate: true),
        new("medical-certificate", "Aviz medical", DocumentGroup.Personal,
            [DocumentCategory.AdeverintaMedicala], HasIssueDate: true, HasExpiryDate: true),
        new("psychological-certificate", "Aviz psihologic", DocumentGroup.Personal,
            [DocumentCategory.AvizPsihologic], HasIssueDate: true, HasExpiryDate: true),

        // ── Documente PFA ──
        new("registration-certificate", "Certificat de înregistrare PFA", DocumentGroup.Pfa,
            [DocumentCategory.CertificatInregistrare], HasIssueDate: true, HasExpiryDate: false),
        new("constatator", "Certificat constatator", DocumentGroup.Pfa,
            [DocumentCategory.CertificatConstatator], HasIssueDate: true, HasExpiryDate: false),
        new("vat-certificate", "Certificat TVA intracomunitar", DocumentGroup.Pfa,
            [DocumentCategory.CertificatTvaIntracomunitar], HasIssueDate: true, HasExpiryDate: false),

        // ── Documente mașină ──
        new("registration-document", "Talon / Certificat de înmatriculare", DocumentGroup.Vehicle,
            [DocumentCategory.Talon, DocumentCategory.ITP], HasIssueDate: true, HasExpiryDate: true),
        new("vehicle-identity-card", "Carte de identitate a vehiculului", DocumentGroup.Vehicle,
            [DocumentCategory.CarteIdentitateAuto], HasIssueDate: true, HasExpiryDate: false),
        new("rca", "RCA", DocumentGroup.Vehicle,
            [DocumentCategory.RCA], HasIssueDate: true, HasExpiryDate: true),
        new("passenger-insurance", "Asigurare călători și bagaje", DocumentGroup.Vehicle,
            [DocumentCategory.AsigurareCalatori], HasIssueDate: true, HasExpiryDate: true),
        new("casco", "CASCO", DocumentGroup.Vehicle,
            [DocumentCategory.Casco], HasIssueDate: true, HasExpiryDate: true, IsOptional: true),
        new("copie-conforma", "Copie conformă", DocumentGroup.Vehicle,
            [DocumentCategory.CopieConforma], HasIssueDate: true, HasExpiryDate: true),
        new("ecuson-uber", "Ecuson Uber", DocumentGroup.Vehicle,
            [DocumentCategory.EcusonUber], HasIssueDate: true, HasExpiryDate: true),
        new("ecuson-bolt", "Ecuson Bolt", DocumentGroup.Vehicle,
            [DocumentCategory.EcusonBolt], HasIssueDate: true, HasExpiryDate: true),
    ];

    public static IReadOnlyList<DocumentTypeDef> ForGroup(DocumentGroup group) =>
        All.Where(d => d.Group == group).ToList();

    public static bool TryParseGroup(string? value, out DocumentGroup group)
    {
        group = DocumentGroup.Personal;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out group);
    }
}
