using Domain.Documents;

namespace Application.Documents.AiVerification;

/// <summary>Un câmp de business pe care OCR-ul îl extrage din document pentru precompletare.</summary>
public sealed record ExtractedFieldSpec(
    string Key,
    string Description,
    ExtractedFieldType Type,
    bool Required,
    bool Sensitive = false);

public sealed record DocumentAiExpectation(
    string Label,
    string Details,
    bool ExpectsExpiryDate,
    IReadOnlyList<ExtractedFieldSpec>? Fields = null,
    // Auto-respingerea la verdict negativ e opțională per categorie — la ~20 de categorii
    // auto-respingerea în lanț ar produce respingeri false. Rămâne activă pentru „tip greșit/ilizibil”.
    bool AutoRejectOnFailure = true)
{
    public IReadOnlyList<ExtractedFieldSpec> FieldSpecs => Fields ?? [];
}

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
            "Carte de identitate românească a unei persoane fizice: conține fotografie, nume, prenume și dată de expirare. NU extrage CNP-ul sau seria/numărul actului.",
            true,
            [
                new("date_of_birth", "Data nașterii titularului (o poți deduce din CNP fără a returna CNP-ul)", ExtractedFieldType.Date, Required: false, Sensitive: true),
                new("full_name", "Numele și prenumele titularului", ExtractedFieldType.Text, Required: false),
            ]),
        [DocumentCategory.CarteIdentitate] = new(
            "Carte de identitate",
            "Carte de identitate românească a unei persoane fizice: conține fotografie, nume, prenume și dată de expirare. NU extrage CNP-ul sau seria/numărul actului.",
            true,
            [
                new("date_of_birth", "Data nașterii titularului (o poți deduce din CNP fără a returna CNP-ul)", ExtractedFieldType.Date, Required: false, Sensitive: true),
                new("full_name", "Numele și prenumele titularului", ExtractedFieldType.Text, Required: false),
            ]),
        [DocumentCategory.PermisConducere] = new(
            "Permis de conducere",
            "Permis de conducere românesc/UE: conține fotografie, categorii de vehicule și date (4b = expirare per categorie, 10 = data obținerii categoriei). NU extrage numărul actului.",
            true,
            [
                new("category_b_obtained_on", "Data obținerii categoriei B (coloana 10 din tabelul de categorii)", ExtractedFieldType.Date, Required: false),
                new("driving_categories", "Categoriile deținute (ex. B, BE)", ExtractedFieldType.Text, Required: false),
                new("licence_expires_on", "Data de expirare a permisului (4b)", ExtractedFieldType.Date, Required: false),
            ]),
        [DocumentCategory.AtestatSofer] = new(
            "Atestat de șofer (transport alternativ)",
            "Certificat/atestat profesional pentru conducător auto de transport alternativ (ridesharing), emis de ARR, cu perioadă de valabilitate.",
            true,
            [
                new("atestat_expires_on", "Data de expirare a atestatului", ExtractedFieldType.Date, Required: false),
            ]),
        [DocumentCategory.AtestatTransport] = new(
            "Atestat / Certificat de transport",
            "Certificat de competență profesională sau atestat pentru transport rutier emis de ARR, cu perioadă de valabilitate.",
            true,
            [
                new("atestat_expires_on", "Data de expirare a atestatului", ExtractedFieldType.Date, Required: false),
            ]),
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
            true,
            [
                new("plate_number", "Numărul de înmatriculare al vehiculului asigurat", ExtractedFieldType.Plate, Required: false),
            ]),
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
            false,
            [
                new("cui", "Codul unic de înregistrare (CUI/CIF), fără prefixul RO", ExtractedFieldType.Cui, Required: true),
                new("legal_name", "Denumirea completă a PFA-ului/entității", ExtractedFieldType.Text, Required: false),
                new("caen_codes", "Toate codurile CAEN ale obiectului de activitate, separate prin virgulă, cel principal primul (ex. 4939)", ExtractedFieldType.Caen, Required: true),
            ]),
        [DocumentCategory.CertificatConstatator] = new(
            "Certificat constatator",
            "Certificat constatator emis de ONRC pentru un PFA/firmă, cu date despre activitate (coduri CAEN).",
            false,
            [
                new("cui", "Codul unic de înregistrare (CUI/CIF), fără prefixul RO", ExtractedFieldType.Cui, Required: false),
                new("registry_number", "Numărul de ordine în registrul comerțului (ex. F40/…/2024)", ExtractedFieldType.Text, Required: false),
                new("caen_codes", "Toate codurile CAEN ale obiectului de activitate, separate prin virgulă, cel principal primul (ex. 4939)", ExtractedFieldType.Caen, Required: true),
            ]),
        [DocumentCategory.DovadaPlataArr] = new(
            "Dovadă plată ARR",
            "Dovadă de plată către ARR (ordin de plată, chitanță, confirmare de plată) pentru autorizația de transport alternativ.",
            false),
        [DocumentCategory.AutorizatieTransportAlternativ] = new(
            "Autorizație transport alternativ",
            "Autorizația pentru transport alternativ emisă de ARR pe numele PFA-ului/operatorului, cu perioadă de valabilitate.",
            true,
            [
                new("authorization_number", "Numărul autorizației de transport alternativ", ExtractedFieldType.Text, Required: false),
                new("authorization_expires_on", "Data de expirare a autorizației", ExtractedFieldType.Date, Required: false),
            ]),
        [DocumentCategory.CopieConforma] = new(
            "Copie conformă",
            "Copia conformă a autorizației de transport alternativ, emisă de ARR pentru un vehicul anume, cu perioadă de valabilitate.",
            true,
            [
                new("copy_conforma_number", "Seria/numărul copiei conforme", ExtractedFieldType.Text, Required: false),
                new("copy_conforma_expires_on", "Data de expirare a copiei conforme", ExtractedFieldType.Date, Required: false),
            ]),
        [DocumentCategory.Talon] = new(
            "Talon (Certificat de înmatriculare)",
            "Certificatul de înmatriculare (talonul) al unui vehicul: conține numărul de înmatriculare, marca, seria de șasiu (VIN) și deținătorul.",
            false,
            [
                new("plate_number", "Numărul de înmatriculare (ex. B123ABC)", ExtractedFieldType.Plate, Required: true),
                new("vin", "Seria de șasiu (VIN), 17 caractere", ExtractedFieldType.Vin, Required: true),
                new("make", "Marca vehiculului", ExtractedFieldType.Text, Required: false),
                new("model", "Modelul vehiculului", ExtractedFieldType.Text, Required: false),
            ]),
        [DocumentCategory.CarteIdentitateAuto] = new(
            "Carte de identitate a vehiculului (CIV)",
            "Cartea de identitate a vehiculului (CIV) emisă de RAR: conține seria de șasiu (VIN), marca și istoricul deținătorilor. Nu are dată de expirare.",
            false,
            [
                new("vin", "Seria de șasiu (VIN), 17 caractere", ExtractedFieldType.Vin, Required: true),
                new("make", "Marca vehiculului", ExtractedFieldType.Text, Required: false),
            ]),
        [DocumentCategory.ContractVehicul] = new(
            "Contract vehicul",
            "Contract pentru folosința vehiculului (comodat, închiriere sau proprietate) între deținător și utilizator, semnat de părți.",
            false),
        [DocumentCategory.ExtrasBancar] = new(
            "Extras de cont / confirmare IBAN",
            "Extras de cont bancar sau document de confirmare a IBAN-ului pentru contul PFA-ului, cu IBAN-ul și titularul contului.",
            false,
            [
                new("iban", "IBAN-ul contului (24 de caractere pentru România)", ExtractedFieldType.Iban, Required: true),
            ],
            // Un extras bancar valid nu trebuie respins automat dacă modelul nu recunoaște „tipul” exact.
            AutoRejectOnFailure: false),
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

    /// <summary>Specificația unui câmp extras, după categorie + cheie (pentru normalizare/aplicare la confirmare).</summary>
    public static ExtractedFieldSpec? FieldSpec(DocumentCategory category, string key) =>
        For(category)?.FieldSpecs.FirstOrDefault(f =>
            string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
}
