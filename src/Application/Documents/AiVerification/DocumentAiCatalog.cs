using Domain.Documents;

namespace Application.Documents.AiVerification;

/// <summary>Un câmp de business pe care OCR-ul îl extrage din document pentru precompletare.</summary>
public sealed record ExtractedFieldSpec(
    string Key,
    string Description,
    ExtractedFieldType Type,
    bool Required,
    bool Sensitive = false);

/// <param name="IssueDateOnly">
/// Documentul nu expiră: data de pe el este a eliberării. Certificatele ONRC intră aici — o dată
/// veche pe ele nu înseamnă nimic, singurul lucru imposibil e să fi fost eliberate în viitor.
/// </param>
public sealed record DocumentAiExpectation(
    string Label,
    string Details,
    bool ExpectsExpiryDate,
    IReadOnlyList<ExtractedFieldSpec>? Fields = null,
    // Auto-respingerea la verdict negativ e opțională per categorie — la ~20 de categorii
    // auto-respingerea în lanț ar produce respingeri false. Rămâne activă pentru „tip greșit/ilizibil”.
    bool AutoRejectOnFailure = true,
    bool IssueDateOnly = false,
    // Documente valabile un număr fix de luni de la eliberare (cazierul: 6). Calculul se face
    // în C#, nu de model — un LLM nu are ce căuta în aritmetica pe date.
    int? ValidMonthsFromIssue = null,
    // Cum arată actul emis de instituție (antet, ștampilă, cod de verificare). Modelul îl compară
    // cu ce vede, ca o foaie scrisă în Word să nu treacă drept adeverință doar pentru că are textul.
    string? AuthenticityHints = null,
    // Elementele fără de care actul ajunge la admin ca suspect.
    AuthenticityMarkers RequiredMarkers = AuthenticityMarkers.None,
    // Actul vine firesc ca PDF generat de o aplicație, fără scan (RO CEI Reader, cazierul
    // electronic): regula „PDF scris într-un editor de text” nu i se aplică.
    bool DigitalPdfExpected = false,
    // Expirarea se calculează în C# din câmpul acesta, nu din „data de expirare” a modelului. Pe
    // talon modelul alegea orice dată îi ieșea în cale — de obicei a înmatriculării —, deși ITP-ul
    // stă în rubrica lui, cu ștampilele stațiilor.
    string? ExpiryFromField = null)
{
    public IReadOnlyList<ExtractedFieldSpec> FieldSpecs => Fields ?? [];
}

/// <summary>Elementele care fac un act oficial, cerute per categorie.</summary>
[Flags]
public enum AuthenticityMarkers
{
    None = 0,

    /// <summary>Antetul unității emitente: denumire, adresă, CUI sau cod.</summary>
    Letterhead = 1,

    /// <summary>Ștampilă, parafă, semnătură olografă sau semnătură electronică — oricare.</summary>
    StampOrSignature = 2,

    /// <summary>Număr de înregistrare sau de serie al actului.</summary>
    RegistrationNumber = 4,
}

/// <summary>
/// Descrie, per categorie, ce ar trebui să conțină documentul încărcat, pentru
/// prevalidarea automată cu AI. Categoriile absente nu sunt trimise la verificare.
/// </summary>
public static class DocumentAiCatalog
{
    /// <summary>Câmpul talonului cu datele ITP; expirarea talonului e cea mai îndepărtată dintre ele.</summary>
    public const string TalonItpField = "itp_valabil_pana_la_toate";

    /// <summary>Actele din care se ia identitatea de referință: buletinul, în oricare formă.</summary>
    public static readonly DocumentCategory[] IdentityCategories =
        [DocumentCategory.CarteIdentitate, DocumentCategory.Buletin, DocumentCategory.CeiReaderPdf];

    /// <summary>
    /// Cartea de identitate e singurul document din care se citesc date de identitate complete:
    /// dosarul de înființare a societății (ramura „Nu am PFA") le cere pe toate, iar șoferul nu
    /// trebuie să le mai tasteze. CNP-ul și seria/numărul actului sunt marcate <c>Sensitive</c> —
    /// se stochează criptate și nu apar niciodată în clar în răspunsuri sau în loguri.
    /// </summary>
    private const string IdentityCardDetails =
        "Carte de identitate românească a unei persoane fizice: conține fotografie, nume, prenume, " +
        "CNP, seria și numărul actului, autoritatea emitentă, datele de emitere și expirare și " +
        "adresa de domiciliu. Se acceptă și cartea de identitate electronică (CEI), inclusiv PDF-ul " +
        "generat de aplicația RO CEI Reader cu datele citite din cip — pe CEI domiciliul nu e " +
        "tipărit pe card, ci apare doar în acest PDF.";

    private static readonly ExtractedFieldSpec[] IdentityCardFields =
    [
        new("date_of_birth", "Data nașterii titularului. Dacă nu e tipărită separat, o iei din CNP: cifrele 2–7 sunt AALLZZ, iar prima cifră dă secolul (1/2 = 1900–1999, 3/4 = 1800–1899, 5/6 = 2000–2099). Ex.: 5010519… = 2001-05-19", ExtractedFieldType.Date, Required: false, Sensitive: true),
        new("full_name", "Numele și prenumele titularului, împreună", ExtractedFieldType.Text, Required: false),
        new("nume", "Numele de familie, separat", ExtractedFieldType.Text, Required: false),
        new("prenume", "Prenumele, separat", ExtractedFieldType.Text, Required: false),
        new("cnp", "CNP-ul titularului, 13 cifre", ExtractedFieldType.Cnp, Required: false, Sensitive: true),
        new("serie_act", "Seria actului, 2 litere (câmpul SERIA)", ExtractedFieldType.Text, Required: false, Sensitive: true),
        new("numar_act", "Numărul actului (câmpul NR): 6 cifre pe cartea clasică; pe cea electronică, cum apare în document", ExtractedFieldType.Text, Required: false, Sensitive: true),
        new("autoritate_emitenta", "Autoritatea emitentă (câmpul „emisă de” / SPCLEP)", ExtractedFieldType.Text, Required: false),
        new("data_emiterii", "Data emiterii actului", ExtractedFieldType.Date, Required: false),
        new("data_expirarii", "Data expirării actului", ExtractedFieldType.Date, Required: false),
        new("domiciliu_judet", "Județul din adresa de domiciliu (fără prefixul „jud.”)", ExtractedFieldType.Text, Required: false),
        new("domiciliu_localitate", "Localitatea din adresa de domiciliu (municipiu, oraș sau comună/sat)", ExtractedFieldType.Text, Required: false),
        new("domiciliu_strada", "Strada din adresa de domiciliu (fără prefixul „str.”)", ExtractedFieldType.Text, Required: false),
        new("domiciliu_numar", "Numărul poștal din adresa de domiciliu (poate fi „12A” sau „FN”)", ExtractedFieldType.Text, Required: false),
        new("domiciliu_bloc", "Blocul, dacă apare", ExtractedFieldType.Text, Required: false),
        new("domiciliu_scara", "Scara, dacă apare", ExtractedFieldType.Text, Required: false),
        new("domiciliu_etaj", "Etajul, dacă apare", ExtractedFieldType.Text, Required: false),
        new("domiciliu_apartament", "Apartamentul, dacă apare", ExtractedFieldType.Text, Required: false),
        // Zona MRZ: cifrele ei de control se verifică în C# (`MrzValidator`). Un act editat rar le
        // nimerește, iar datele din ea trebuie să fie aceleași cu cele tipărite.
        new("mrz_raw", "Zona citibilă automat (MRZ): rândurile cu caractere „<” din partea de jos a cărții clasice (2 rânduri de 36) sau de pe versoul cărții electronice (3 rânduri de 30). Transcrie-le EXACT, caracter cu caracter, fiecare rând separat prin „|”. null dacă nu se vede", ExtractedFieldType.Text, Required: false, Sensitive: true),
    ];

    private const string IdentityCardHints =
        "Actul real e un card tipărit pe suport securizat: fotografie integrată în card, model de " +
        "fundal fin, stema României, zona MRZ cu caractere „<”. O poză de pe ecran, o fotocopie " +
        "editată sau un card cu fotografia lipită sunt suspecte.";

    private const string MedicalHints =
        "Adeverința reală are antetul unității medicale (denumire, adresă, CUI sau cod), număr de " +
        "înregistrare, numele și CNP-ul pacientului, concluzia (apt/inapt), parafa medicului cu " +
        "codul de parafă și ștampila unității — sau, ca PDF digital, semnătura electronică a " +
        "unității. Un text simplu pe foaie albă, fără antet, parafă și ștampilă, nu e o adeverință.";

    private const string ArrHints =
        "Emis de Autoritatea Rutieră Română (ARR), pe card sau formular tipizat, cu serie și număr.";

    /// <summary>Titularul unui act personal, ca să-l putem compara cu buletinul.</summary>
    private static readonly ExtractedFieldSpec HolderName =
        new("titular", "Numele și prenumele persoanei căreia îi aparține documentul (titularul / pacientul / solicitantul)", ExtractedFieldType.Text, Required: false);

    private static readonly ExtractedFieldSpec HolderCnp =
        new("cnp_titular", "CNP-ul titularului, 13 cifre, dacă apare pe document", ExtractedFieldType.Cnp, Required: false, Sensitive: true);

    private static readonly Dictionary<DocumentCategory, DocumentAiExpectation> Expectations = new()
    {
        [DocumentCategory.Buletin] = new(
            "Buletin (Carte de identitate)",
            IdentityCardDetails,
            true,
            IdentityCardFields,
            AuthenticityHints: IdentityCardHints,
            DigitalPdfExpected: true),
        [DocumentCategory.CarteIdentitate] = new(
            "Carte de identitate",
            IdentityCardDetails,
            true,
            IdentityCardFields,
            AuthenticityHints: IdentityCardHints,
            DigitalPdfExpected: true),
        // PDF-ul din RO CEI Reader, lângă poza față-verso a cărții electronice. Aceleași câmpuri:
        // comparația lor cu poza arată că PDF-ul e chiar al cărții fotografiate.
        [DocumentCategory.CeiReaderPdf] = new(
            "PDF RO CEI Reader",
            "PDF generat de aplicația RO CEI Reader cu datele citite din cipul cărții de identitate " +
            "electronice: nume, prenume, CNP, seria și numărul, datele de emitere și expirare, domiciliul.",
            true,
            IdentityCardFields,
            AuthenticityHints:
                "E un PDF generat de aplicație, cu datele din cip așezate într-un tabel și, de regulă, " +
                "fotografia titularului. Nu e o poză și nici un scan.",
            DigitalPdfExpected: true),
        [DocumentCategory.PermisConducere] = new(
            "Permis de conducere",
            "Permis de conducere românesc/UE: conține fotografie, categorii de vehicule și date (4b = expirare per categorie, 10 = data obținerii categoriei). NU extrage numărul actului.",
            true,
            [
                // Coloana 10, pe rândul B: de aici se calculează vechimea de 2 ani. Modelul lua
                // uneori rândul AM/B1 sau data emiterii permisului (4a), care e mai recentă.
                new("category_b_obtained_on", "Data obținerii categoriei B: pe VERSO, în tabelul de categorii, rândul cu simbolul „B” (nu B1, nu BE, nu AM), coloana 10. NU data 4a (emiterea permisului) și NU coloana 11", ExtractedFieldType.Date, Required: true),
                new("permis_emis_la_4a", "Data emiterii permisului, câmpul 4a de pe față", ExtractedFieldType.Date, Required: false),
                new("driving_categories", "Categoriile deținute (ex. B, BE)", ExtractedFieldType.Text, Required: false),
                new("licence_expires_on", "Data de expirare a permisului (4b)", ExtractedFieldType.Date, Required: false),
                new("titular_nume", "Numele de familie al titularului (câmpul 1)", ExtractedFieldType.Text, Required: false),
                new("titular_prenume", "Prenumele titularului (câmpul 2)", ExtractedFieldType.Text, Required: false),
                new("titular_data_nasterii", "Data nașterii titularului (câmpul 3, fără locul nașterii)", ExtractedFieldType.Date, Required: false),
                new("cnp_titular", "CNP-ul titularului (câmpul 4d), 13 cifre", ExtractedFieldType.Cnp, Required: false, Sensitive: true),
            ],
            AuthenticityHints:
                "Permisul real e un card din policarbonat, model UE: steagul UE cu „RO”, fotografie " +
                "gravată, fundal cu model fin, câmpurile numerotate 1–12. Pe verso, tabelul categoriilor."),
        [DocumentCategory.AtestatSofer] = new(
            "Atestat de șofer (transport alternativ)",
            "Certificat/atestat profesional pentru conducător auto de transport alternativ (ridesharing), emis de ARR, cu perioadă de valabilitate.",
            true,
            [
                new("atestat_expires_on", "Data de expirare a atestatului", ExtractedFieldType.Date, Required: false),
                HolderName,
                HolderCnp,
            ],
            AuthenticityHints: ArrHints),
        [DocumentCategory.AtestatTransport] = new(
            "Atestat / Certificat de transport",
            "Certificat de competență profesională sau atestat pentru transport rutier emis de ARR, cu perioadă de valabilitate.",
            true,
            [
                new("atestat_expires_on", "Data de expirare a atestatului", ExtractedFieldType.Date, Required: false),
                HolderName,
                HolderCnp,
            ],
            AuthenticityHints: ArrHints),
        // Fără câmpuri, „valid” însemna doar „seamănă cu o adeverință”: trecea și o foaie scrisă
        // în Word. Acum se citesc titularul, emitentul și concluzia, iar antetul și ștampila sau
        // semnătura sunt obligatorii.
        [DocumentCategory.AdeverintaMedicala] = new(
            "Adeverință medicală",
            "Adeverință sau aviz medical (și/sau psihologic) pentru conducător auto, emisă de o unitate medicală, de regulă cu dată de emitere recentă sau valabilitate.",
            true,
            [
                HolderName,
                HolderCnp,
                new("unitate_emitenta", "Unitatea medicală care a emis adeverința (clinică, cabinet, spital)", ExtractedFieldType.Text, Required: true),
                new("cod_parafa_medic", "Codul de parafă sau numele medicului care semnează", ExtractedFieldType.Text, Required: false),
                new("concluzie", "Concluzia: „apt”, „inapt” sau „apt condiționat”, exact cum e scrisă", ExtractedFieldType.Text, Required: false),
            ],
            AuthenticityHints: MedicalHints,
            RequiredMarkers: AuthenticityMarkers.Letterhead | AuthenticityMarkers.StampOrSignature),
        [DocumentCategory.AvizPsihologic] = new(
            "Aviz psihologic",
            "Aviz psihologic pentru conducător auto, emis de un cabinet de psihologie autorizat, cu dată de emitere și valabilitate.",
            true,
            [
                HolderName,
                HolderCnp,
                new("unitate_emitenta", "Cabinetul de psihologie care a emis avizul", ExtractedFieldType.Text, Required: true),
                new("cod_parafa_medic", "Codul psihologului (atestatul din Colegiul Psihologilor) sau numele lui", ExtractedFieldType.Text, Required: false),
                new("concluzie", "Concluzia: „apt”, „inapt” sau „apt condiționat”, exact cum e scrisă", ExtractedFieldType.Text, Required: false),
            ],
            AuthenticityHints:
                "Avizul real are antetul cabinetului, număr de înregistrare, parafa psihologului cu codul " +
                "din Colegiul Psihologilor și ștampila sau semnătura lui.",
            RequiredMarkers: AuthenticityMarkers.Letterhead | AuthenticityMarkers.StampOrSignature),
        [DocumentCategory.CazierJudiciar] = new(
            "Cazier judiciar",
            "Certificat de cazier judiciar emis de Poliția Română. Raportează data emiterii și, dacă apare explicit, data de valabilitate.",
            true,
            [
                HolderName,
                HolderCnp,
                // Sursa de rezervă pentru agenția ARR, când certificatul de înregistrare n-are un
                // județ lizibil: cazierul se ridică de la poliția județului de domiciliu.
                new("judet", "Județul unității de poliție emitente, fără prefixul „jud.”", ExtractedFieldType.Text, Required: false),
            ],
            // Valabil 6 luni de la eliberare; termenul îl calculează serverul, nu modelul.
            ValidMonthsFromIssue: 6,
            AuthenticityHints:
                "Certificatul real e emis de Poliția Română (IGPR / inspectoratul județean): formular " +
                "tipizat cu serie și număr, ștampilă și semnătură, sau varianta electronică semnată " +
                "digital, cu cod de verificare.",
            RequiredMarkers: AuthenticityMarkers.StampOrSignature,
            DigitalPdfExpected: true),
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
            ],
            DigitalPdfExpected: true),
        [DocumentCategory.Casco] = new(
            "Poliță CASCO",
            "Poliță de asigurare CASCO pentru un vehicul, emisă de un asigurător, cu perioada de valabilitate.",
            true,
            AutoRejectOnFailure: false,
            DigitalPdfExpected: true),
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
                new("registry_number", "Numărul de ordine în registrul comerțului (ex. F40/…/2024)", ExtractedFieldType.Text, Required: false),
                new("holder_name", "Titularul PFA-ului (persoana fizică), nume și prenume", ExtractedFieldType.Text, Required: false),
                new("professional_office", "Sediul profesional, ca text, exact cum apare pe certificat", ExtractedFieldType.Text, Required: false),
                // Județul sediului, separat de adresa completă: de aici se precompletează agenția
                // ARR la care se depune dosarul, iar un text liber n-ar fi putut alimenta un select.
                new("judet", "Județul sediului profesional, fără prefixul „jud.”", ExtractedFieldType.Text, Required: false),
                new("caen_codes", "Toate codurile CAEN ale obiectului de activitate, separate prin virgulă, cel principal primul (ex. 4933)", ExtractedFieldType.Caen, Required: true),
            ],
            // Data de pe certificat e a eliberării: un certificat de înregistrare nu expiră.
            IssueDateOnly: true,
            DigitalPdfExpected: true),
        [DocumentCategory.CertificatConstatator] = new(
            "Certificat constatator",
            "Certificat constatator emis de ONRC pentru un PFA/firmă, cu activitățile autorizate, sediul profesional și eventualele puncte de lucru.",
            false,
            [
                new("cui", "Codul unic de înregistrare (CUI/CIF), fără prefixul RO", ExtractedFieldType.Cui, Required: false),
                new("registry_number", "Numărul de ordine în registrul comerțului (ex. F40/…/2024)", ExtractedFieldType.Text, Required: false),
                new("caen_codes", "Toate codurile CAEN ale obiectului de activitate, separate prin virgulă, cel principal primul (ex. 4933)", ExtractedFieldType.Caen, Required: true),
                new("authorized_activities", "Activitățile autorizate, cu denumirea lor, separate prin punct și virgulă", ExtractedFieldType.Text, Required: false),
                new("professional_office", "Sediul profesional, ca text, exact cum apare pe certificat", ExtractedFieldType.Text, Required: false),
                new("activity_location", "Unde se desfășoară activitatea: „la sediu”, „la terți” sau ambele, cum e menționat", ExtractedFieldType.Text, Required: false),
                new("work_points", "Punctele de lucru declarate, separate prin punct și virgulă; gol dacă nu există", ExtractedFieldType.Text, Required: false),
            ],
            IssueDateOnly: true),
        [DocumentCategory.RezolutieOnrc] = new(
            "Rezoluție / încheiere ONRC",
            "Rezoluția sau încheierea emisă de ONRC la înființarea PFA-ului. Se păstrează pentru arhivă — datele utile vin din cele două certificate.",
            false,
            [
                new("cui", "Codul unic de înregistrare (CUI/CIF), fără prefixul RO", ExtractedFieldType.Cui, Required: false),
                new("registry_number", "Numărul de ordine în registrul comerțului (ex. F40/…/2024)", ExtractedFieldType.Text, Required: false),
            ],
            IssueDateOnly: true),
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
            "Certificatul de înmatriculare (talonul) al unui vehicul: conține numărul de înmatriculare, marca, seria de șasiu (VIN) și deținătorul. " +
            "Are o rubrică separată „Inspecția tehnică periodică” (de regulă în partea dreaptă), unde stațiile ITP pun ștampile sau autocolante cu data până la care e valabilă inspecția.",
            false,
            [
                new("plate_number", "Numărul de înmatriculare (ex. B123ABC)", ExtractedFieldType.Plate, Required: true),
                new("vin", "Seria de șasiu (VIN), 17 caractere", ExtractedFieldType.Vin, Required: true),
                new("make", "Marca vehiculului", ExtractedFieldType.Text, Required: false),
                new("model", "Modelul vehiculului", ExtractedFieldType.Text, Required: false),
                // B și I se cer doar ca modelul să le țină separat de ITP: înainte, una dintre ele
                // ajungea drept „expirare”, iar talonul se putea respinge pe data înmatriculării.
                new("data_prima_inmatriculare", "Data primei înmatriculări (câmpul B)", ExtractedFieldType.Date, Required: false),
                new("data_inmatriculare", "Data înmatriculării (câmpul I)", ExtractedFieldType.Date, Required: false),
                new("itp_valabil_pana_la_toate", "TOATE datele „valabil până la” din rubrica „Inspecția tehnică periodică” (ștampilele sau autocolantele stațiilor ITP, de regulă în partea dreaptă a talonului), în format YYYY-MM-DD, separate prin virgulă. NU pune aici data B, data I sau alte date de pe talon. null dacă rubrica e goală sau nu se vede", ExtractedFieldType.Text, Required: false),
            ],
            AuthenticityHints: "Formular tipizat DRPCIV pe hârtie securizată, cu serie, câmpurile codificate A–Z și rubrica ITP.",
            ExpiryFromField: TalonItpField),
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
        // Bonul de cheltuială: singura categorie unde extragerea e scopul, nu o prevalidare.
        // Sumele vin ca text brut, exact cum apar pe hârtie — MoneyParser le transformă în
        // numere, în C#. Modelul nu calculează TVA, nu convertește monede și nu decide dacă
        // data e plauzibilă; toate astea sunt aritmetică, iar el n-are ce căuta în ea.
        [DocumentCategory.Cheltuiala] = new(
            "Bon fiscal sau factură de cheltuială",
            "Un bon fiscal, o factură sau o chitanță pentru o cheltuială a PFA-ului: combustibil, " +
            "încărcare electrică, service, piese, spălătorie, asigurare, telefon, software sau altele. " +
            "Conține denumirea comerciantului, data, suma totală și, de obicei, TVA-ul.",
            ExpectsExpiryDate: false,
            [
                new("supplier_name", "Denumirea comerciantului sau a furnizorului, așa cum apare pe document", ExtractedFieldType.Text, Required: false),
                new("supplier_cui", "CUI-ul furnizorului, dacă apare", ExtractedFieldType.Cui, Required: false),
                new("document_date", "Data documentului", ExtractedFieldType.Date, Required: false),
                new("total_amount", "Suma totală de plată, exact cum e scrisă pe document, fără să o recalculezi", ExtractedFieldType.Text, Required: false),
                new("vat_amount", "Valoarea TVA, exact cum e scrisă pe document; lasă gol dacă nu apare explicit", ExtractedFieldType.Text, Required: false),
                new("currency", "Moneda documentului (RON, EUR)", ExtractedFieldType.Text, Required: false),
                new("document_type", "Tipul documentului: bon fiscal, factură sau chitanță", ExtractedFieldType.Text, Required: false),
                new("document_number", "Numărul bonului sau seria și numărul facturii, exact cum sunt scrise", ExtractedFieldType.Text, Required: false),
                new("beneficiary_cui", "CUI/CIF client al cumpărătorului, dacă este înscris; nu confunda cu CUI furnizor", ExtractedFieldType.Cui, Required: false),
                new("items_description", "Produsele sau serviciile cumpărate, așa cum apar pe document", ExtractedFieldType.Text, Required: false),
                new("expense_category", "Propune categoria după produsele cumpărate: FUEL pentru benzină/motorină/GPL, CAR_SERVICE pentru service/piese/anvelope, CAR_INSURANCE pentru RCA/CASCO, CAR_WASH pentru spălătorie, CAR_RENTAL pentru chirie auto, EV_CHARGING pentru încărcare electrică auto, ACCOUNTING pentru contabilitate, SOFTWARE pentru abonamente software ale activității, PHONE pentru telefonie, CASH_REGISTER pentru casă de marcat/consumabile, PERSONAL pentru cumpărături personale. Lasă gol dacă nu se potrivește. Nu decide deductibilitatea fiscală.", ExtractedFieldType.Text, Required: false),
                new("personal_amount", "Suma articolelor personale (cafea, alimente, tutun) explicit identificate separat pe document, dacă sunt prezente; lasă gol dacă nu există sau nu poți citi sumele", ExtractedFieldType.Text, Required: false),
            ],
            // Un bon nu se respinge pentru că modelul nu-i recunoaște „tipul”: extragerea
            // eșuată lasă formularul editabil manual, nu blochează adăugarea cheltuielii.
            AutoRejectOnFailure: false,
            IssueDateOnly: true),
        [DocumentCategory.CertificatTvaIntracomunitar] = new(
            "Certificat de TVA intracomunitar / decizie ANAF",
            "Certificatul de înregistrare în scopuri de TVA pentru operațiuni intracomunitare (cod special art. 317) sau decizia ANAF de atribuire a codului, emisă pe numele PFA-ului.",
            false,
            // Codul special e un CUI prefixat cu RO — nu-l extragem, îl verifică un om față de dosar.
            AutoRejectOnFailure: false),
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
