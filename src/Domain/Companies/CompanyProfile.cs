using SharedKernel;

namespace Domain.Companies;

/// <summary>
/// Identitatea publică și juridică a unui proprietar de anunțuri.
/// </summary>
/// <remarks>
/// Una singură pentru toate tipurile de cont, discriminată prin <see cref="OwnerType"/>, nu două
/// tabele paralele (spec §7.1). Un PFA care listează mașini are aceleași nevoi ca un SRL — logo,
/// slug, date de contact — iar cardul din marketplace le citește identic, fără să întrebe cine e
/// proprietarul.
///
/// Datele de identitate trăiesc **aici**, nu în setările dashboardului: un CUI editabil din două
/// locuri ajunge, în timp, două CUI-uri diferite pe două documente.
/// </remarks>
public sealed class CompanyProfile : Entity
{
    public Guid Id { get; set; }

    /// <summary>Contul căruia îi aparține profilul. Relație unu-la-unu.</summary>
    public Guid UserId { get; set; }

    public OwnerType OwnerType { get; set; } = OwnerType.Srl;

    /// <summary>Denumirea juridică — cea care intră în contracte și facturi.</summary>
    public string LegalName { get; set; } = string.Empty;
    public string? Cui { get; set; }
    public string? RegCom { get; set; }
    public string? LegalRepresentative { get; set; }
    public string? RegisteredOffice { get; set; }

    /// <summary>
    /// Contul în care se încasează chiriile. Intră în contracte și în facturi.
    /// </summary>
    /// <remarks>
    /// În clar, spre deosebire de IBAN-urile din onboardingul PFA, care se criptează: acela e al
    /// unei persoane fizice, ăsta e al firmei și apare oricum tipărit pe fiecare document pe care
    /// firma îl trimite. Criptarea lui ar fi protejat ceva ce firma publică singură.
    /// </remarks>
    public string? Iban { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? PublicDescription { get; set; }

    /// <summary>Fraza scurtă de sub denumire, în antetul mini-site-ului.</summary>
    /// <remarks>
    /// Separată de <see cref="PublicDescription"/> fiindcă are altă treabă: descrierea se citește,
    /// sloganul se vede. Într-un singur câmp, primele opt cuvinte ale descrierii ar fi devenit
    /// titlu, iar cine scria un paragraf bun ajungea cu un titlu prost.
    /// </remarks>
    public string? Tagline { get; set; }

    /// <summary>Fotografia de fundal din antetul mini-site-ului. Cale relativă, ca logo-ul.</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Locul de unde se preiau mașinile, cu adresă și pin pe hartă.
    /// </summary>
    /// <remarks>
    /// Separat de <see cref="RegisteredOffice"/> dinadins: sediul social e o adresă juridică, de
    /// multe ori a contabilului, și e supus comutatorului de vizibilitate. Ăsta e locul în care
    /// omul chiar vine să ia mașina — n-are comutator, fiindcă a-l completa **este** actul de a-l
    /// publica, iar el se completează într-o secțiune care se numește „Unde ne găsiți".
    /// </remarks>
    public string? PickupAddress { get; set; }

    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }

    /// <summary>Indicația practică de lângă hartă: „intrarea din spate", „lângă benzinărie".</summary>
    public string? PickupNote { get; set; }

    /// <summary>Culorile mini-site-ului. Nu e niciodată <c>null</c>: fără nimic salvat, sunt cele implicite.</summary>
    public CompanyPageTheme PageTheme { get; set; } = new();

    /// <summary>Conținutul secțiunilor proprii ale mini-site-ului.</summary>
    public CompanyPageContent PageContent { get; set; } = new();

    /// <summary>
    /// Verdictul administrării asupra paginii și secțiunile blocate de ea.
    /// </summary>
    /// <remarks>
    /// Câmpurile de mai sus — slogan, descriere, culori, secțiuni, cover, punct de preluare — sunt
    /// **ciorna** proprietarului, nu ce se vede pe internet. Ce se vede stă în
    /// <see cref="PublishedPage"/> și ajunge acolo doar printr-o aprobare.
    /// </remarks>
    public CompanyPageModeration PageModeration { get; set; } = new();

    /// <summary>Copia aprobată a paginii. <c>ApprovedAtUtc == null</c> înseamnă că n-a fost publicată niciodată.</summary>
    public CompanyPagePublication PublishedPage { get; set; } = new();

    /// <summary>Calea logo-ului încărcat. <c>null</c> nu e o eroare — atunci se afișează inițialele.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Specimenul de semnătură al firmei, ca document criptat.</summary>
    /// <remarks>
    /// Se tipărește pe contractele și procesele-verbale generate, ca proprietarul să nu semneze de
    /// mână fiecare document. Spre deosebire de logo, nu stă niciodată în clar pe disc: e o
    /// semnătură, nu o marcă.
    /// <para>
    /// Înlocuirea creează un document nou; cel vechi rămâne, fiindcă documentele deja tipărite cu el
    /// trebuie să se poată retipări identic.
    /// </para>
    /// </remarks>
    public Guid? SignatureDocumentId { get; set; }

    /// <summary>
    /// Identitatea din URL-ul mini-site-ului: <c>/f/{slug}</c>. Unic.
    /// </summary>
    /// <remarks>
    /// Nu se regenerează la schimbarea denumirii. Un slug care se mută rupe fiecare link deja
    /// trimis către pagina firmei, iar aceia sunt exact linkurile pe care proprietarul le-a
    /// distribuit ca să fie găsit (spec §4.2).
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Verificarea firmei de către RIDElance. Se acordă manual, din administrare.</summary>
    public bool IsVerified { get; set; }

    // Vizibilitatea publică, câmp cu câmp. Un singur flag „public/privat" ar fi forțat
    // proprietarul să aleagă între a-și arăta telefonul și a-și arăta adresa.
    public bool ShowPhone { get; set; } = true;
    public bool ShowEmail { get; set; } = true;
    public bool ShowWhatsApp { get; set; } = true;
    public bool ShowLocation { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
