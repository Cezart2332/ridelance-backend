namespace Domain.Companies;

/// <summary>
/// Unde a ajuns mini-site-ul unei firme în drumul lui către public.
/// </summary>
/// <remarks>
/// Pagina firmei e singurul loc din platformă în care un cont scrie text liber și încarcă o
/// fotografie care ajung apoi pe un domeniu al nostru, indexate, lângă marca RIDElance. De aceea
/// nu se publică singură: proprietarul scrie o ciornă, iar administrarea decide dacă versiunea aia
/// pleacă mai departe.
/// </remarks>
public enum CompanyPageReviewStatus
{
    /// <summary>Nimic de citit încă — profil nou sau pagină golită de tot.</summary>
    Draft = 0,

    /// <summary>Proprietarul a salvat ceva și așteaptă verificarea.</summary>
    Pending = 1,

    /// <summary>Versiunea din <see cref="CompanyPagePublication"/> e cea aprobată.</summary>
    Approved = 2,

    /// <summary>Refuzată. Nu există versiune publicată, iar motivul stă în <see cref="CompanyPageModeration.Note"/>.</summary>
    Rejected = 3,
}

/// <summary>
/// Verdictul administrării asupra mini-site-ului, plus secțiunile blocate.
/// </summary>
/// <remarks>
/// <see cref="BlockedSections"/> supraviețuiește oricărei salvări a proprietarului: o secțiune
/// oprită de noi rămâne oprită până o deblocăm tot noi. Altfel butonul de blocat n-ar fi o decizie,
/// ci o sugestie — proprietarul ar reactiva-o la următoarea editare.
///
/// Se salvează ca jsonb pe profil, lângă restul personalizării paginii.
/// </remarks>
public sealed class CompanyPageModeration
{
    public CompanyPageReviewStatus Status { get; set; } = CompanyPageReviewStatus.Draft;

    /// <summary>Id-uri din <see cref="CompanyPageSections.Blockable"/>.</summary>
    public List<string> BlockedSections { get; set; } = [];

    /// <summary>
    /// Motivul, scris de administrare și citit de proprietar în editorul lui.
    /// </summary>
    /// <remarks>
    /// Un refuz fără motiv e un refuz pe care proprietarul nu-l poate remedia — ar retrimite
    /// aceeași pagină, iar noi am refuza-o din nou.
    /// </remarks>
    public string? Note { get; set; }

    /// <summary>Când a fost trimisă spre verificare versiunea curentă.</summary>
    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}

/// <summary>
/// Copia aprobată a paginii — exact ce vede vizitatorul.
/// </summary>
/// <remarks>
/// Separată de câmpurile de pe profil, care sunt ciorna proprietarului. Fără copia asta, prima
/// literă tastată după o aprobare ar fi scos pagina de pe internet până la următoarea verificare;
/// așa, versiunea aprobată rămâne live cât timp cea nouă își așteaptă rândul.
///
/// Conține doar ce scrie sau încarcă proprietarul liber. Denumirea, CUI-ul, telefonul și e-mailul
/// nu sunt aici: sunt date de identitate, se servesc direct de pe profil și nu au ce modera.
/// </remarks>
public sealed class CompanyPagePublication
{
    /// <summary>Când a fost aprobată versiunea asta. <c>null</c> = pagina n-a fost publicată niciodată.</summary>
    public DateTime? ApprovedAtUtc { get; set; }

    public string? Tagline { get; set; }
    public string? PublicDescription { get; set; }

    /// <summary>Fotografia de fundal, așa cum era la aprobare.</summary>
    public string? CoverImageUrl { get; set; }

    public CompanyPageTheme Theme { get; set; } = new();
    public CompanyPageContent Content { get; set; } = new();

    public string? PickupAddress { get; set; }
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public string? PickupNote { get; set; }
}

/// <summary>
/// Secțiunile mini-site-ului care se pot bloca din administrare.
/// </summary>
/// <remarks>
/// Id-urile sunt aceleași cu cele din <c>src/components/company/sections.ts</c> — cele două liste
/// trebuie ținute sincronizate, ca la cheile de iconiță.
///
/// „Flota" și „Contact" lipsesc dinadins. Flota arată doar anunțuri deja aprobate, care au propriul
/// drum de verificare, iar contactul arată doar datele pe care proprietarul le-a marcat publice în
/// Profil. Nici una nu e text scris liber, deci n-are ce bloca aici.
/// </remarks>
public static class CompanyPageSections
{
    public const string About = "despre";
    public const string Highlights = "avantaje";
    public const string Schedule = "program";
    public const string Faq = "intrebari";
    public const string Location = "locatie";

    public static readonly IReadOnlySet<string> Blockable = new HashSet<string>(StringComparer.Ordinal)
    {
        About, Highlights, Schedule, Faq, Location,
    };
}
