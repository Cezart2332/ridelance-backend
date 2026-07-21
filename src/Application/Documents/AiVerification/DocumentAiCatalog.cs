using Domain.Documents;

namespace Application.Documents.AiVerification;

public sealed record DocumentAiExpectation(string Label, string Details, bool ExpectsExpiryDate);

/// <summary>
/// Descrie, per categorie, ce ar trebui să conțină documentul încărcat, pentru
/// prevalidarea automată cu AI. Categoriile absente nu sunt trimise la verificare.
/// </summary>
public static class DocumentAiCatalog
{
    private static readonly Dictionary<DocumentCategory, DocumentAiExpectation> Expectations = new()
    {
        [DocumentCategory.Buletin] = new(
            "Buletin (Carte de identitate)",
            "Carte de identitate românească a unei persoane fizice: conține fotografie, nume, prenume și dată de expirare.",
            true),
        [DocumentCategory.CarteIdentitate] = new(
            "Carte de identitate",
            "Carte de identitate românească a unei persoane fizice: conține fotografie, nume, prenume și dată de expirare.",
            true),
        [DocumentCategory.PermisConducere] = new(
            "Permis de conducere",
            "Permis de conducere românesc/UE: conține fotografie, categorii de vehicule și dată de expirare (4b).",
            true),
        [DocumentCategory.AtestatSofer] = new(
            "Atestat de șofer (transport alternativ)",
            "Certificat/atestat profesional pentru conducător auto de transport alternativ (ridesharing), emis de ARR, cu perioadă de valabilitate.",
            true),
        [DocumentCategory.AtestatTransport] = new(
            "Atestat / Certificat de transport",
            "Certificat de competență profesională sau atestat pentru transport rutier emis de ARR, cu perioadă de valabilitate.",
            true),
        [DocumentCategory.AdeverintaMedicala] = new(
            "Adeverință medicală",
            "Adeverință sau aviz medical (și/sau psihologic) pentru conducător auto, emisă de o unitate medicală, de regulă cu dată de emitere recentă sau valabilitate.",
            true),
        [DocumentCategory.CazierJudiciar] = new(
            "Cazier judiciar",
            "Certificat de cazier judiciar emis de Poliția Română. Este valabil 6 luni de la data emiterii — dacă găsești doar data emiterii, calculează expirarea la 6 luni după aceasta.",
            true),
        [DocumentCategory.ITP] = new(
            "ITP (Inspecția Tehnică Periodică)",
            "Dovada ITP a unui vehicul: anexa/talonul cu viza ITP sau raportul de inspecție tehnică, cu data următoarei inspecții (data expirării).",
            true),
        [DocumentCategory.RCA] = new(
            "Poliță RCA",
            "Poliță de asigurare RCA pentru un vehicul, cu numărul de înmatriculare și perioada de valabilitate (dată de sfârșit).",
            true),
        [DocumentCategory.AsigurareCalatori] = new(
            "Asigurare de persoane/călători",
            "Poliță de asigurare pentru persoanele transportate (asigurare de accidente a călătorilor), cu perioadă de valabilitate.",
            true),
        [DocumentCategory.EcusonUber] = new(
            "Ecuson Uber",
            "Ecusonul (autocolantul/legitimația) Uber pentru vehicul, emis pentru transport alternativ, de regulă cu dată de valabilitate.",
            true),
        [DocumentCategory.EcusonBolt] = new(
            "Ecuson Bolt",
            "Ecusonul (autocolantul/legitimația) Bolt pentru vehicul, emis pentru transport alternativ, de regulă cu dată de valabilitate.",
            true),
        [DocumentCategory.CertificatInregistrare] = new(
            "Certificat de înregistrare (CUI)",
            "Certificat de înregistrare fiscală al unui PFA/firmei emis de ONRC/ANAF, cu CUI și denumirea entității. Nu are dată de expirare.",
            false),
        [DocumentCategory.CertificatConstatator] = new(
            "Certificat constatator",
            "Certificat constatator emis de ONRC pentru un PFA/firmă, cu date despre activitate (coduri CAEN).",
            false),
        [DocumentCategory.DovadaPlataArr] = new(
            "Dovadă plată ARR",
            "Dovadă de plată către ARR (ordin de plată, chitanță, confirmare de plată) pentru autorizația de transport alternativ.",
            false),
        [DocumentCategory.AutorizatieTransportAlternativ] = new(
            "Autorizație transport alternativ",
            "Autorizația pentru transport alternativ emisă de ARR pe numele PFA-ului/operatorului, cu perioadă de valabilitate.",
            true),
        [DocumentCategory.CopieConforma] = new(
            "Copie conformă",
            "Copia conformă a autorizației de transport alternativ, emisă de ARR pentru un vehicul anume, cu perioadă de valabilitate.",
            true),
        [DocumentCategory.Talon] = new(
            "Talon (Certificat de înmatriculare)",
            "Certificatul de înmatriculare (talonul) al unui vehicul: conține numărul de înmatriculare, marca, seria de șasiu (VIN) și deținătorul.",
            false),
        [DocumentCategory.CarteIdentitateAuto] = new(
            "Carte de identitate a vehiculului (CIV)",
            "Cartea de identitate a vehiculului (CIV) emisă de RAR: conține seria de șasiu (VIN), marca și istoricul deținătorilor. Nu are dată de expirare.",
            false),
        [DocumentCategory.ContractVehicul] = new(
            "Contract vehicul",
            "Contract pentru folosința vehiculului (comodat, închiriere sau proprietate) între deținător și utilizator, semnat de părți.",
            false),
        [DocumentCategory.AcordLeasing] = new(
            "Acord leasing",
            "Acordul societății de leasing pentru utilizarea vehiculului în activitatea de transport alternativ.",
            false),
        [DocumentCategory.DovadaPlataCopieConformaEcusoane] = new(
            "Dovadă plată copie conformă & ecusoane",
            "Dovadă de plată (ordin de plată, chitanță, confirmare) pentru copia conformă și/sau ecusoane.",
            false),
    };

    public static bool IsEligible(DocumentCategory category) => Expectations.ContainsKey(category);

    public static DocumentAiExpectation? For(DocumentCategory category) =>
        Expectations.TryGetValue(category, out DocumentAiExpectation? expectation) ? expectation : null;

    public static string LabelFor(DocumentCategory category) =>
        For(category)?.Label ?? category.ToString();
}
